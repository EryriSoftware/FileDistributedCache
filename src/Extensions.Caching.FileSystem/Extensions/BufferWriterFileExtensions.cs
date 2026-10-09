using System.Buffers;

namespace Eryri.Extensions.Caching.FileSystem.Extensions;

internal static class BufferWriterFileExtensions
{
    private const int BufferSize = 4 << 10; // 4 KiB
    extension(IBufferWriter<byte> writer)
    {
        public async ValueTask ReadFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            for (int read = 0; (read = await file.ReadAsync(writer.GetMemory(BufferSize), cancellationToken)) > 0; writer.Advance(read))
            { }
        }

        public void ReadFile(string path)
        {
            using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                options: FileOptions.SequentialScan);

            for (int read = 0; (read = file.Read(writer.GetSpan(BufferSize))) > 0; writer.Advance(read))
            { }
        }
    }
}
