using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem.Domain;

internal class CacheDirectoryOwner : IDisposable
{
    private bool isDisposed = false;
    public bool IsPersistent { get; private set; } = true;
    public readonly DirectoryInfo Directory;
    public CacheDirectoryOwner(IOptions<FileCacheOptions> options)
    {
        if (options.Value.CacheDirectory is { } cacheDirectory)
        {
            Directory = new DirectoryInfo(cacheDirectory);
            IsPersistent = true;
            return;
        }
        else
        {
            Directory = System.IO.Directory.CreateTempSubdirectory();
            IsPersistent = false;
        }
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            if (!IsPersistent)
            {
                Directory.Delete(recursive: true);
            }
        }
    }
}
