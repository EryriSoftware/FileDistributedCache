using System.Buffers;
using Eryri.Extensions.Caching.FileSystem.Extensions;
using Eryri.Extensions.Caching.FileSystem.Models;
using Eryri.WriteAheadLog;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain.Persistence;

internal class WalPersistence : WriteAheadLog<Mutation>, IPersistence
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
        AppendAsync(new Mutation(MutationType.Insert, value), cancellationToken);
    public ValueTask Update(Metadata value, CancellationToken cancellationToken) =>
        AppendAsync(new Mutation(MutationType.Update, value), cancellationToken);
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken) =>
        AppendAsync(new Mutation(MutationType.Delete, value), cancellationToken);

    protected override ValueTask ApplyAsync(Mutation mutation, CancellationToken cancellationToken)
    {
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

    protected override async ValueTask ReadSnapshotAsync(Stream stream, CancellationToken cancellationToken)
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

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await base.InitializeAsync(cancellationToken);
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

    protected override void Serialize(Mutation mutation, IBufferWriter<byte> buffer) => buffer.Write(mutation);

    protected override Mutation Deserialize(ReadOnlyMemory<byte> record)
    {
        record.Span.ReadMutation(out var mutation);
        return mutation;
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
