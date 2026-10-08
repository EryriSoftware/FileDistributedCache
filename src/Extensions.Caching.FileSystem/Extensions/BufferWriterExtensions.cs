using System.Buffers;
using Eryri.Buffers.Extensions;
using Eryri.Extensions.Caching.FileSystem.Extensions;
using Eryri.Extensions.Caching.FileSystem.Models;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class BufferWriterExtensions
{
    public static void Write(this IBufferWriter<byte> writer, Mutation value)
    {
        writer.Write([(byte)value.Type]);
        writer.Write(value.Value);
    }

    public static void Write(this IBufferWriter<byte> writer, Metadata value)
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
    }

    public static void Write(this IBufferWriter<byte> writer, int value) => Buffers.Extensions.BufferWriterExtensions.Write(writer, value); // Provide valid signature for int
    public static void Write(this IBufferWriter<byte> writer, long value) => Buffers.Extensions.BufferWriterExtensions.Write(writer, value); // Provide valid signature for long
    public static void Write(this IBufferWriter<byte> writer, long? value)
    {
        writer.Write(value.HasValue);

        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }

    public static int ReadMutation(this ReadOnlySpan<byte> reader, out Mutation value)
    {
        var offset = 1;
        var type = (MutationType)reader[0];
        offset += reader.Slice(offset).ReadMetadata(out var metadata);

        value = new Mutation(type, metadata);
        return offset;
    }

    public static int ReadMetadata(this ReadOnlySpan<byte> reader, out Metadata value)
    {
        var offset = 0;
        offset += reader.Slice(offset).ReadString(out var key);
        offset += reader.Slice(offset).ReadString(out var path);
        offset += reader.Slice(offset).ReadLong(out var sizeBytes);
        offset += reader.Slice(offset).ReadLong(out var createdUtcTicks);
        offset += reader.Slice(offset).ReadULong(out var version);
        offset += reader.Slice(offset).ReadULong(out var accessCount);
        offset += reader.Slice(offset).ReadLong(out var lastAccessUtcTicks);
        offset += reader.Slice(offset).ReadMaybeLong(out var absoluteExpirationTicks);
        offset += reader.Slice(offset).ReadMaybeLong(out var slidingExpirationTicks);
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

        return offset;
    }

    public static int ReadMaybeLong(this ReadOnlySpan<byte> reader, out long? value)
    {
        var offset = reader.ReadBoolean(out var hasValue);
        if (!hasValue)
        {
            value = null;
            return offset;
        }

        offset += reader.Slice(offset).ReadLong(out var actual);
        value = actual;
        return offset;
    }
}
