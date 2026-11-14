using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Infrastructure;

namespace NOF.Hosting;

public static partial class NOFInfrastructureExtensions
{
    extension(ObjectStorageSelector selector)
    {
        /// <summary>Uses durable local object storage with an optional custom directory.</summary>
        public ObjectStorageSelector AddFileSystemObjectStorage(Action<FileSystemObjectStorageOptions>? configureOptions = null)
        {
            var services = selector.Services;
            services.AddOptions<FileSystemObjectStorageOptions>();
            if (configureOptions is not null)
            {
                services.Configure(configureOptions);
            }

            services.ReplaceOrAddScoped<IObjectStorageRider, FileSystemObjectStorageRider>();
            return selector;
        }

        /// <summary>Uses host-isolated, in-memory object storage, primarily for tests.</summary>
        public ObjectStorageSelector AddMemoryObjectStorage()
        {
            var services = selector.Services;
            services.TryAddSingleton<MemoryObjectStorageRiderState>();
            services.ReplaceOrAddScoped<IObjectStorageRider>(sp => new MemoryObjectStorageRider(
                sp.GetRequiredService<MemoryObjectStorageRiderState>()));
            return selector;
        }
    }
}
