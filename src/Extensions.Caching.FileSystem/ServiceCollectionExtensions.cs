using Eryri.Extensions.Caching.FileSystem.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Eryri.Extensions.Caching.FileSystem;

/// <summary>
/// Extension methods for setting up file cache related services in an <see cref="IServiceCollection" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds a non distributed file system implementation of <see cref="IDistributedCache"/> to the
        /// <see cref="IServiceCollection" />.
        /// </summary>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public IServiceCollection AddDistributedFileCache()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddOptionsWithValidateOnStart<FileCacheOptions>(nameof(FileCacheOptions));
            services.TryAddSingleton<CacheDirectoryOwner>();
            services.TryAddSingleton<Manifest>();
            services.TryAddSingleton<IPersistence>(sp =>
            {
                var directoryOwner = sp.GetRequiredService<CacheDirectoryOwner>();

                return directoryOwner.IsPersistent
                    ? ActivatorUtilities.CreateInstance<WalPersistence>(sp)
                    : ActivatorUtilities.CreateInstance<NoPersistence>(sp);
            });
            services.TryAddSingleton<FileDistributedCache>();
            services.TryAddSingleton<IFileDistributedCache>(sp => sp.GetRequiredService<FileDistributedCache>());
            services.TryAddSingleton<IDistributedCache>(sp => sp.GetRequiredService<FileDistributedCache>());
            services.TryAddSingleton<IBufferDistributedCache>(sp => sp.GetRequiredService<FileDistributedCache>());

            return services;
        }

        /// <summary>
        /// Adds a non distributed file system implementation of <see cref="IDistributedCache"/> to the
        /// <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="setupAction">
        /// The <see cref="Action{FileCacheOptions}"/> to configure the provided <see cref="FileCacheOptions"/>.
        /// </param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public IServiceCollection AddDistributedFileCache(Action<FileCacheOptions> setupAction) => services
            .AddDistributedFileCache()
            .Configure(setupAction);
    }
}
