using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MigrationTools
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddConfiguredService(this IServiceCollection collection, IConfiguration config)
        {
            return collection;
        }
    }
}
