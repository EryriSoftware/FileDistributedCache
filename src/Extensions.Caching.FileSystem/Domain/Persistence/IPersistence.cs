using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal interface IPersistence
{
    public ValueTask Insert(Metadata value, CancellationToken cancellationToken);
    public ValueTask Update(Metadata value, CancellationToken cancellationToken);
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken);
    public ValueTask RestoreSnapshotAsync(CancellationToken cancellationToken);
}
