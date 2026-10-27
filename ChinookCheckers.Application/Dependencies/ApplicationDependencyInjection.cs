using ChinookCheckers.Application.Caching;
using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Application.Services;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // One shared instance for the whole process: the cache must outlive the scoped MoveService.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider =>
        {
            var cacheOptions = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
            return new LruCache<string, SuggestMoveResponse>(
                cacheOptions.Capacity,
                TimeSpan.FromMinutes(cacheOptions.TtlMinutes),
                provider.GetRequiredService<TimeProvider>());
        });

        services.AddSingleton<ISearchLimitsResolver, SearchLimitsResolver>();
        services.AddScoped<IMoveService, MoveService>();

        return services;
    }
}
