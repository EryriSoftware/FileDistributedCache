using System.Text.Json;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class ManifestPersistance(Manifest manifest, CacheDirectoryOwner directoryOwner) : WriteAheadLog(directoryOwner.Directory.FullName)
{
    public ValueTask Insert(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Insert, cancellationToken);
    public ValueTask Update(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Update, cancellationToken);
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken) =>
        Append(value, MutationType.Delete, cancellationToken);

    private async ValueTask Append(Metadata value, MutationType type, CancellationToken cancellationToken)
    {
        var log = new Mutation(type, value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(log, Mutation.TypeInfo);
        await AppendAsync(bytes, cancellationToken);
    }

    protected override ValueTask ApplyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var mutation = JsonSerializer.Deserialize(data.Span, Mutation.TypeInfo)!;

        if (mutation.Kind == MutationType.Insert)
        {
            manifest.AddOrReplace(mutation.Value, out _);
        }
        else if (mutation.Kind == MutationType.Update)
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
        else if (mutation.Kind == MutationType.Delete)
        {
            manifest.TryRemove(mutation.Value);
        }

        return ValueTask.CompletedTask;
    }

    protected override async ValueTask ReadSnapshotAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var values = await JsonSerializer.DeserializeAsync(
            stream,
            JsonContext.Default.ICollectionMetadata,
            cancellationToken);

        foreach (var item in values!)
        {
            manifest.TryAdd(item);
        }
    }

    protected override async ValueTask WriteSnapshotAsync(Stream stream, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(
            stream,
            manifest.Values,
            JsonContext.Default.ICollectionMetadata,
            cancellationToken);
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
}
