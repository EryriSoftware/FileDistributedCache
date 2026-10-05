using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class StreamExtensions
{
    public static void Write(this Stream stream, int value)
    {
        using var buffer = MemoryPool<byte>.Shared.Rent(4);
        var header = buffer.Memory.Slice(0, 4).Span;
        BinaryPrimitives.WriteInt32LittleEndian(header, value);
        stream.Write(header);
    }

    public static async Task Write(this Stream stream, ICollection<Metadata> values, CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();

        writer.Write(values.Count);
        await stream.WriteAsync(writer.WrittenMemory, cancellationToken);

        foreach (var item in values)
        {
            writer.Clear();
            writer.Write(item);
            await stream.WriteAsync(writer.WrittenMemory, cancellationToken);
        }
    }

    public static async IAsyncEnumerable<Metadata> ReadMetadataValues(this Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = stream.ReadInt();

        for (var i = 0; i < count; i++)
        {
            yield return stream.ReadMetadata();
        }
    }

    public static Mutation ReadMutation(this Stream stream)
    {
        var buffer = MemoryPool<byte>.Shared.Rent(1);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, 1));
        var type = (MutationType)buffer.Memory.Span[0];

        var metadata = stream.ReadMetadata();
        return new Mutation(type, metadata);
    }

    public static Metadata ReadMetadata(this Stream stream)
    {
        return new Metadata(
            Key: stream.ReadString(),
            Path: stream.ReadString(),
            SizeBytes: stream.ReadLong(),
            CreatedTicks: stream.ReadLong())
        {
            Version = stream.ReadULong(),
            AccessCount = stream.ReadULong(),
            LastAccessTicks = stream.ReadLong(),
            AbsoluteExpirationTicks = stream.ReadMaybeLong(),
            SlidingExpirationTicks = stream.ReadMaybeLong(),
        };
    }

    public static string ReadString(this Stream stream)
    {
        var length = stream.ReadInt();
        var buffer = MemoryPool<byte>.Shared.Rent(length);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, length));
        return Encoding.UTF8.GetString(buffer.Memory.Span.Slice(0, length));
    }

    public static long? ReadMaybeLong(this Stream stream)
    {
        var buffer = MemoryPool<byte>.Shared.Rent(1);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, 1));

        return BitConverter.ToBoolean(buffer.Memory.Span.Slice(0, 1))
            ? stream.ReadLong()
            : null;
    }

    public static long ReadLong(this Stream stream)
    {
        var buffer = MemoryPool<byte>.Shared.Rent(8);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, 8));
        return BinaryPrimitives.ReadInt64LittleEndian(buffer.Memory.Span.Slice(0, 8));
    }

    public static ulong ReadULong(this Stream stream)
    {
        var buffer = MemoryPool<byte>.Shared.Rent(8);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, 8));
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer.Memory.Span.Slice(0, 8));
    }

    public static int ReadInt(this Stream stream)
    {
        var buffer = MemoryPool<byte>.Shared.Rent(4);
        stream.ReadExactly(buffer.Memory.Span.Slice(0, 4));
        return BinaryPrimitives.ReadInt32LittleEndian(buffer.Memory.Span.Slice(0, 4));
    }
}
