namespace Eryri.Extensions.Caching.FileSystem.Wal;

internal interface IWriteAheadLog<T>
{
    public ValueTask InitializeAsync(CancellationToken cancellationToken);
    public ValueTask AppendAsync(T record, CancellationToken cancellationToken);
    public ValueTask AppendAsync(T record, Durability durability, CancellationToken cancellationToken);
    public ValueTask SaveSnapshotAsync(CancellationToken cancellationToken);
    public Task FlushAsync(CancellationToken cancellationToken);
}