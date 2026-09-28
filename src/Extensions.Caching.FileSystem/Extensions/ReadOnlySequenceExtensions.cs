using System.Buffers;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class ReadOnlySequenceExtensions
{
    private const int BufferSize = 4 << 10; // 4 KiB
    extension(ReadOnlySequence<byte> sequence)
    {
        public async ValueTask WriteAllBytesAsync(string path, CancellationToken cancellationToken)
        {
            await using var file = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous);

            foreach (var segment in sequence)
            {
                await file.WriteAsync(segment, cancellationToken);
            }
        }

        public void WriteAllBytes(string path)
        {
            using var file = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BufferSize);

            foreach (var segment in sequence)
            {
                file.Write(segment.Span);
            }
        }
    }
}
