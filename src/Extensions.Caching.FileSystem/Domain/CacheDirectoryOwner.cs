using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class CacheDirectoryOwner : IDisposable
{
    private bool isDisposed = false;
    private bool shouldDisposeDirectory = true;
    public readonly DirectoryInfo Directory;
    public CacheDirectoryOwner(IOptions<FileCacheOptions> options)
    {
        if (options.Value.CacheDirectory is { } cacheDirectory)
        {
            Directory = new DirectoryInfo(cacheDirectory);
            shouldDisposeDirectory = false;
            return;
        }
        else
        {
            Directory = System.IO.Directory.CreateTempSubdirectory();
            shouldDisposeDirectory = true;
        }
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            if (shouldDisposeDirectory)
            {
                Directory.Delete(recursive: true);
            }
        }
    }
}
