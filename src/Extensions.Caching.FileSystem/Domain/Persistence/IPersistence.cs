using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain.Persistence;

internal interface IPersistence
{
    public void Insert(Metadata value);
    public void Update(Metadata value);
    public void Delete(Metadata value);
    public Task InitializeAsync(CancellationToken cancellationToken);
}
