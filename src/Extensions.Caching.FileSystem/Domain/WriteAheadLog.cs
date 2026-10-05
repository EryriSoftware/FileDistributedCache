using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
using Eryri.Extensions.Caching.FileSystem.Extensions;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal abstract class WriteAheadLog : IDisposable, IAsyncDisposable
{
    protected abstract ulong FormatVersion { get; }
    protected bool IsDisposed { get; private set; }
    private bool hasChanges = false;
    private const int BufferSize = 64 << 10; // 64 KB
    private readonly string directory;
    private string LogPath => Path.Combine(directory, "state.wal");
    private string SnapshotPath => Path.Combine(directory, "snapshot.wal");
    private string VersionPath => Path.Combine(directory, "version.wal");
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
        CheckVersion();

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

    private void CheckVersion()
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        if (File.Exists(VersionPath))
        {
            using var fs = File.OpenRead(VersionPath);

            if (fs.Length != 8)
            {
                CleanCache();
            }
            else
            {
                fs.ReadExactly(buffer);
                var currentVersion = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
                if (currentVersion != FormatVersion)
                {
                    CleanCache();
                }
            }
        }

        BinaryPrimitives.WriteUInt64LittleEndian(buffer, FormatVersion);
        File.WriteAllBytes(VersionPath, buffer);

        void CleanCache()
        {
            Directory.Delete(directory, recursive: true);
            Directory.CreateDirectory(directory);
        }
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
            Interlocked.Exchange(ref hasChanges, true);
            logStream.Write(log.Value.Length);
            await logStream.WriteAsync(log.Value, cancellationToken);
        }
        else if (command is FlushCommand flush)
        {
            await logStream.FlushAsync();
            flush.TaskSource.SetResult();
        }
        else if (command is SaveCommand save)
        {
            if (Interlocked.CompareExchange(ref hasChanges, false, true))
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
            }

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

            while (logStream.Position < logStream.Length)
            {
                var length = logStream.ReadInt();
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

    public virtual async ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            await FlushAsync();
            ctSource.Cancel();
            ctSource.Dispose();
            logStream.Dispose();
            sync.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    private interface ICommand { }
    private record LogCommand(ReadOnlyMemory<byte> Value) : ICommand;
    private record FlushCommand(TaskCompletionSource TaskSource) : ICommand;
    private record SaveCommand(TaskCompletionSource TaskSource) : ICommand;
}
