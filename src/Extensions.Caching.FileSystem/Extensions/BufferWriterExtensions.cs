using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Eryri.Extensions.Caching.FileSystem.Extensions;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class BufferWriterExtensions
{
    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, Mutation value)
    {
        writer.Write([(byte)value.Type]);
        writer.Write(value.Value);

        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, Metadata value)
    {
        writer.Write(value.Key);
        writer.Write(value.Path);
        writer.Write(value.SizeBytes);
        writer.Write(value.CreatedTicks);
        writer.Write(value.Version);
        writer.Write(value.AccessCount);
        writer.Write(value.LastAccessTicks);
        writer.Write(value.AbsoluteExpirationTicks);
        writer.Write(value.SlidingExpirationTicks);

        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        writer.Write(byteCount);
        var span = writer.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, span);
        writer.Advance(byteCount);
        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, long? value)
    {
        var span = writer.GetSpan(9);

        span[0] = value.HasValue ? (byte)1 : (byte)0;
        writer.Advance(1);

        if (value.HasValue)
        {
            BinaryPrimitives.WriteInt64LittleEndian(
                span[1..],
                value.GetValueOrDefault());
            writer.Advance(8);
        }

        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(8), value);
        writer.Advance(8);
        return writer;
    }

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(4), value);
        writer.Advance(4);
        return writer;
    }

    public static int ReadMutation(this ReadOnlySpan<byte> reader, out Mutation value)
    {
        var read = 1;
        var type = (MutationType)reader[0];
        read += reader.Slice(read).ReadMetadata(out var metadata);

        value = new Mutation(type, metadata);
        return read;
    }

    public static int ReadMetadata(this ReadOnlySpan<byte> reader, out Metadata value)
    {
        var read = 0;
        read += reader.Slice(read).ReadString(out var key);
        read += reader.Slice(read).ReadString(out var path);
        read += reader.Slice(read).ReadLong(out var sizeBytes);
        read += reader.Slice(read).ReadLong(out var createdUtcTicks);
        read += reader.Slice(read).ReadULong(out var version);
        read += reader.Slice(read).ReadULong(out var accessCount);
        read += reader.Slice(read).ReadLong(out var lastAccessUtcTicks);
        read += reader.Slice(read).ReadMaybeLong(out var absoluteExpirationTicks);
        read += reader.Slice(read).ReadMaybeLong(out var slidingExpirationTicks);
        value = new Metadata(
            Key: key,
            Path: path,
            SizeBytes: sizeBytes,
            CreatedTicks: createdUtcTicks)
        {
            Version = version,
            AccessCount = accessCount,
            LastAccessTicks = lastAccessUtcTicks,
            AbsoluteExpirationTicks = absoluteExpirationTicks,
            SlidingExpirationTicks = slidingExpirationTicks,
        };

        return read;
    }

    public static int ReadString(this ReadOnlySpan<byte> reader, out string value)
    {
        var read = reader.ReadInt(out var length);
        value = Encoding.UTF8.GetString(reader.Slice(read, length));
        return read + length;
    }

    public static int ReadMaybeLong(this ReadOnlySpan<byte> reader, out long? value)
    {
        if (BitConverter.ToBoolean(reader.Slice(0, 1)))
        {
            value = BinaryPrimitives.ReadInt64LittleEndian(reader.Slice(1, 8));
            return 9;
        }

        value = null;
        return 1;
    }

    public static int ReadLong(this ReadOnlySpan<byte> reader, out long value)
    {
        value = BinaryPrimitives.ReadInt64LittleEndian(reader.Slice(0, 8));
        return 8;
    }

    public static int ReadULong(this ReadOnlySpan<byte> reader, out ulong value)
    {
        value = BinaryPrimitives.ReadUInt64LittleEndian(reader.Slice(0, 8));
        return 8;
    }

    public static int ReadInt(this ReadOnlySpan<byte> reader, out int value)
    {
        value = BinaryPrimitives.ReadInt32LittleEndian(reader.Slice(0, 4));
        return 4;
    }
}
