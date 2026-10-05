using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class NoPersistence : IPersistence
{
    public ValueTask Insert(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask Update(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RestoreSnapshotAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
