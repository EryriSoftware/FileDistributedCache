using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Domain.Persistence;

internal class NoPersistence : IPersistence
{
    public ValueTask Insert(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask Update(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask Delete(Metadata value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
