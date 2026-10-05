using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
using Eryri.Extensions.Caching.FileSystem.Extensions;

namespace Eryri.Extensions.Caching.FileSystem.Domain.Persistence;

internal abstract class WriteAheadLog<T> : IDisposable, IAsyncDisposable
{
    protected abstract ulong FormatVersion { get; }
    protected bool IsDisposed { get; private set; }
    private bool hasChanges = false;
    private const int BufferSize = 64 << 10; // 64 KB
    private readonly string directory;
    private string LogPath => Path.Combine(directory, "state.wal");
    private string SnapshotPath => Path.Combine(directory, "snapshot.bin");
    private string VersionPath => Path.Combine(directory, "format.version");
    private readonly FileStream logStream;
    private readonly SemaphoreSlim sync = new (1, 1);
    private readonly CancellationTokenSource ctSource = new CancellationTokenSource();
    private readonly Task consumer;
    private ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>(BufferSize);
    private readonly Channel<Command> channel = Channel
        .CreateUnbounded<Command>(
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
        consumer = ConsumerLoop(ctSource.Token);
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
            Delete(LogPath);
            Delete(SnapshotPath);
            Delete(VersionPath);
        }

        void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

    private void ResetBuffer()
    {
        if (buffer.Capacity > BufferSize)
        {
            buffer = new ArrayBufferWriter<byte>(BufferSize);
        }
        else
        {
            buffer.ResetWrittenCount();
        }
    }

    private async ValueTask HandleCommand(Command command, CancellationToken cancellationToken)
    {
        if (command is AppendCommand log)
        {
            Interlocked.Exchange(ref hasChanges, true);
            Serialize(log.Value, buffer);
            logStream.Write(buffer.WrittenCount);
            await logStream.WriteAsync(buffer.WrittenMemory, cancellationToken);
            ResetBuffer();

            if (log.durability == Durability.Flush)
            {
                await logStream.FlushAsync(cancellationToken);
            }
        }
        else if (command is FlushCommand flush)
        {
            await logStream.FlushAsync();
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
        }

        command.Complete();
    }

    public ValueTask AppendAsync(T record, CancellationToken cancellationToken) => AppendAsync(record, Durability.None, cancellationToken);
    public async ValueTask AppendAsync(T record, Durability durability, CancellationToken cancellationToken)
    {
        var command = new AppendCommand(record, durability);
        await channel.Writer.WriteAsync(command, cancellationToken);

        if (durability != Durability.None)
        {
            await command.Task.WaitAsync(cancellationToken);
        }
    }

    protected abstract void Serialize(T record, IBufferWriter<byte> buffer);
    protected abstract T Deserialize(ReadOnlyMemory<byte> record);
    protected abstract ValueTask ApplyAsync(T record, CancellationToken cancellationToken);
    protected abstract ValueTask WriteSnapshotAsync(Stream stream, CancellationToken cancellationToken);
    protected abstract ValueTask ReadSnapshotAsync(Stream stream, CancellationToken cancellationToken);

    public async ValueTask SaveSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var command = new SaveCommand();
        await channel.Writer.WriteAsync(command, cancellationToken);
        await command.Task.WaitAsync(cancellationToken);
    }

    public virtual async ValueTask InitializeAsync(CancellationToken cancellationToken)
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
                var memory = buffer.GetMemory(length).Slice(0, length);
                await logStream.ReadExactlyAsync(memory, cancellationToken);

                var record = Deserialize(memory);
                await ApplyAsync(record, cancellationToken);
                ResetBuffer();
            }
        }
        finally
        {
            logStream.Seek(0, SeekOrigin.End);
            sync.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var command = new FlushCommand();
        await channel.Writer.WriteAsync(command, cancellationToken);
        await command.Task.WaitAsync(cancellationToken);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            await FlushAsync(ctSource.Token);
            ctSource.Cancel();
            ctSource.Dispose();
            logStream.Dispose();
            sync.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public enum Durability
    {
        /// <summary>Queue the record without waiting for persistence.</summary>
        None,

        /// <summary>Wait until the record has been written to the stream.</summary>
        Write,

        /// <summary>Wait until the record has been flushed.</summary>
        Flush
    }

    private abstract record class Command
    {
        private readonly TaskCompletionSource TaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => TaskSource.SetResult();
        public Task Task => TaskSource.Task;
    }

    private record AppendCommand(T Value, Durability durability) : Command;
    private record FlushCommand : Command;
    private record SaveCommand : Command;
}
