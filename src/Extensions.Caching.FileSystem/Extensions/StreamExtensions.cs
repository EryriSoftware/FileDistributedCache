using System.Buffers;
using System.Runtime.CompilerServices;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class StreamExtensions
{
    public static async Task Write(this Stream stream, ICollection<Metadata> values, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();

        writer.Write(values.Count);

        foreach (var item in values)
        {
            writer.Write(item);
        }

        await stream.WriteAsync(writer.WrittenMemory, cancellationToken);
    }

    public static async IAsyncEnumerable<Metadata> ReadMetadataValues(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var length = checked((int)stream.Length);
        using var buffer = MemoryPool<byte>.Shared.Rent(length);
        var data = buffer.Memory[..length];
        await stream.ReadExactlyAsync(data, cancellationToken);

        var offset = data.Span.ReadInt(out var count);

        for (var i = 0; i < count; i++)
        {
            offset += data.Span.Slice(offset).ReadMetadata(out var metadata);
            yield return metadata;
        }
    }
}
