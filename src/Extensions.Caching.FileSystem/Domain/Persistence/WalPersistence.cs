using System.Buffers;
using Eryri.Extensions.Caching.FileSystem.Extensions;
using Eryri.Extensions.Caching.FileSystem.Models;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain.Persistence;

internal class WalPersistence : WriteAheadLog, IPersistence
{
    private readonly Manifest manifest;
    private readonly CacheDirectoryOwner directoryOwner;
    protected override ulong FormatVersion { get; } = 1;

    private ITimer snapshotSchedule;

    public WalPersistence(
        Manifest manifest,
        CacheDirectoryOwner directoryOwner,
        IOptions<FileCacheOptions> options,
        TimeProvider timeProvider) : base(directoryOwner.Directory.FullName)
    {
        this.manifest = manifest;
        this.directoryOwner = directoryOwner;
        snapshotSchedule = timeProvider.CreateTimer(_ => SaveSnapshotAsync().GetAwaiter().GetResult(), null, options.Value.SnapshotInterval, options.Value.SnapshotInterval);
    }

    public ValueTask Insert(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Insert, cancellationToken);
    public ValueTask Update(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Update, cancellationToken);
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Delete, cancellationToken);

    private async ValueTask Append(Metadata value, MutationType type, CancellationToken cancellationToken)
    {
        var mutation = new Mutation(type, value);
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(mutation);
        await AppendAsync(writer.WrittenSpan.ToArray(), cancellationToken);
    }

    protected override ValueTask ApplyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        data.Span.ReadMutation(out var mutation);

        if (mutation.Type == MutationType.Insert)
        {
            manifest.AddOrReplace(mutation.Value, out _);
        }
        else if (mutation.Type == MutationType.Update)
        {
            while (manifest.TryGetValue(mutation.Value.Key, out var existing)
                && mutation.Value.Version > existing.Version)
            {
                if (manifest.TryUpdate(mutation.Value.Key, mutation.Value, existing))
                {
                    break;
                }
            }
        }
        else if (mutation.Type == MutationType.Delete)
        {
            manifest.TryRemove(mutation.Value);
        }

        return ValueTask.CompletedTask;
    }

    protected override async ValueTask ReadSnapshotAsync(FileStream stream, CancellationToken cancellationToken)
    {
        await foreach (var item in stream.ReadMetadataValues(cancellationToken))
        {
            manifest.TryAdd(item);
        }
    }

    protected override async ValueTask WriteSnapshotAsync(Stream stream, CancellationToken cancellationToken)
    {
        await stream.Write(manifest.Values, cancellationToken);
    }

    public override async ValueTask RestoreSnapshotAsync(CancellationToken cancellationToken)
    {
        await base.RestoreSnapshotAsync(cancellationToken);
        var paths = manifest.Values.Select(x => x.Path).ToHashSet();
        var allFiles = directoryOwner.Directory.GetFiles("*bytes", new EnumerationOptions { RecurseSubdirectories = true });

        foreach (var file in allFiles)
        {
            if (!paths.Contains(file.FullName))
            {
                file.Delete();
            }
        }
    }

    public override ValueTask DisposeAsync()
    {
        if (!IsDisposed)
        {
            snapshotSchedule.DisposeAsync();
        }

        return base.DisposeAsync();
    }
}
