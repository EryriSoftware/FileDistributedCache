using System.Buffers;
using Eryri.Buffers.Extensions;
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

    public static IBufferWriter<byte> Write(this IBufferWriter<byte> writer, long? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }

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

    public static int ReadMaybeLong(this ReadOnlySpan<byte> reader, out long? value)
    {
        var offset = reader.ReadBoolean(out var hasValue);
        if (hasValue)
        {
            offset += reader.ReadLong(out var actual);
            value = actual;
            return offset;
        }

        value = null;
        return offset;
    }
}
