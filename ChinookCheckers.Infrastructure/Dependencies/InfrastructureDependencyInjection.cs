using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Core.Options;
using ChinookCheckers.Infrastructure.Engine;
using ChinookCheckers.Infrastructure.Engine.Kingsrow;
using ChinookCheckers.Infrastructure.Engine.Pool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChinookCheckers.Infrastructure.Dependencies;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EngineOptions>()
            .Bind(configuration.GetSection(EngineOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<KingsrowLibraryProvisioner>();
        services.AddSingleton<IEngineAdapterFactory, KingsrowEngineAdapterFactory>();

        services.AddSingleton<EngineWorkerPool>();
        services.AddSingleton<ICheckersEnginePool>(provider => provider.GetRequiredService<EngineWorkerPool>());
        services.AddHostedService<EngineWorkerPoolInitializer>();

        return services;
    }
}
