namespace Eryri.Extensions.Caching.FileSystem.Wal;

internal enum Durability
{
    /// <summary>Queue the record without waiting for persistence.</summary>
    None,

    /// <summary>Wait until the record has been written to the stream.</summary>
    Write,

    /// <summary>Wait until the record has been flushed.</summary>
    Flush
}
