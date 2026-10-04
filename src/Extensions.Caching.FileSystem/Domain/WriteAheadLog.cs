using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal abstract class WriteAheadLog : IDisposable
{
    private const int BufferSize = 64 << 10; // 64 KB
    private readonly string directory;
    private string LogPath => Path.Combine(directory, $"state.wal");
    private string SnapshotPath => Path.Combine(directory, "snapshot.wal");
    private readonly FileStream logStream;
    private readonly SemaphoreSlim sync = new (1, 1);
    private readonly CancellationTokenSource ctSource = new CancellationTokenSource();
    private readonly Channel<ICommand> channel = Channel
        .CreateUnbounded<ICommand>(
    new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    protected WriteAheadLog(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);

        logStream = new FileStream(
            LogPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous);

        logStream.Seek(0, SeekOrigin.End);
        _ = ConsumerLoop(ctSource.Token);
    }

    private async Task ConsumerLoop(CancellationToken cancellationToken)
    {
        await foreach (var command in channel.Reader.ReadAllAsync(cancellationToken))
        {
            await sync.WaitAsync(cancellationToken);

            try
            {
                await HandleCommand(command, cancellationToken);
            }
            finally
            {
                sync.Release();
            }
        }
    }

    private async ValueTask HandleCommand(ICommand command, CancellationToken cancellationToken)
    {
        if (command is LogCommand log)
        {
            using var buffer = MemoryPool<byte>.Shared.Rent(4);
            var header = buffer.Memory.Slice(0, 4).Span;
            BinaryPrimitives.WriteInt32LittleEndian(header, log.Value.Length);
            logStream.Write(header);
            await logStream.WriteAsync(log.Value, cancellationToken);
        }
        else if (command is FlushCommand flush)
        {
            await logStream.FlushAsync();
            flush.TaskSource.SetResult();
        }
        else if (command is SaveCommand save)
        {
            var tempPath = SnapshotPath + ".tmp";

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan))
            {
                await WriteSnapshotAsync(
                    stream,
                    cancellationToken);
            }

            File.Move(tempPath, SnapshotPath, overwrite: true);
            logStream.Seek(0, SeekOrigin.Begin);
            logStream.SetLength(0);
            save.TaskSource.SetResult();
        }
    }

    public async ValueTask AppendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        await channel.Writer.WriteAsync(new LogCommand(data), cancellationToken);
    }

    protected abstract ValueTask ApplyAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken);

    protected abstract ValueTask WriteSnapshotAsync(
        Stream stream,
        CancellationToken cancellationToken);
    public async ValueTask SaveSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var taskSource = new TaskCompletionSource();
        await channel.Writer.WriteAsync(new SaveCommand(taskSource), cancellationToken);
        await taskSource.Task;
    }

    public virtual async ValueTask RestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);

        try
        {
            if (File.Exists(SnapshotPath))
            {
                await using var stream = new FileStream(
                SnapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                await ReadSnapshotAsync(stream, cancellationToken);
            }

            logStream.Seek(0, SeekOrigin.Begin);

            using var headerBuffer = MemoryPool<byte>.Shared.Rent(4);
            var header = headerBuffer.Memory.Slice(0, 4);

            while (logStream.Position < logStream.Length)
            {
                await logStream.ReadExactlyAsync(header, cancellationToken);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.Span);

                using var buffer = MemoryPool<byte>.Shared.Rent(length);

                var memory = buffer.Memory.Slice(0, length);
                await logStream.ReadExactlyAsync(memory, cancellationToken);
                await ApplyAsync(memory, cancellationToken);
            }
        }
        finally
        {
            logStream.Seek(0, SeekOrigin.End);
            sync.Release();
        }
    }

    protected abstract ValueTask ReadSnapshotAsync(FileStream stream, CancellationToken cancellationToken);

    public async Task FlushAsync()
    {
        var taskSource = new TaskCompletionSource();
        await channel.Writer.WriteAsync(new FlushCommand(taskSource));
        await taskSource.Task;
    }

    public void Dispose()
    {
        FlushAsync().GetAwaiter().GetResult();
        ctSource.Cancel();
        ctSource.Dispose();
        logStream.Dispose();
        sync.Dispose();
    }

    private interface ICommand { }
    private record LogCommand(ReadOnlyMemory<byte> Value) : ICommand;
    private record FlushCommand(TaskCompletionSource TaskSource) : ICommand;
    private record SaveCommand(TaskCompletionSource TaskSource) : ICommand;
}
