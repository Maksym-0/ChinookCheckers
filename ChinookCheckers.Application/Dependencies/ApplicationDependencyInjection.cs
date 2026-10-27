using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Application.Services;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Application.Dependencies;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LimitsOptions>()
            .Bind(configuration.GetSection(LimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Dedicated, size-bounded instance: a global SizeLimit would force every other IMemoryCache user to set Size.
        services.AddKeyedSingleton<IMemoryCache>(MoveService.CacheServiceKey, (provider, _) =>
        {
            var cacheOptions = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            return new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheOptions.Capacity });
        });

        services.AddSingleton<ISearchLimitsResolver, SearchLimitsResolver>();
        services.AddScoped<IMoveService, MoveService>();

        return services;
    }
}
