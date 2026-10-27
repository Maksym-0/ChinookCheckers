using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow;

internal sealed class KingsrowEngineAdapterFactory(
    IOptions<EngineOptions> options,
    KingsrowLibraryProvisioner provisioner,
    ILoggerFactory loggerFactory) : IEngineAdapterFactory
{
    public IEngineAdapter Create(int workerId)
    {
        var engineOptions = options.Value;
        if (!string.Equals(engineOptions.Type, EngineOptions.KingsrowDllType, StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException(
                $"Unsupported engine type '{engineOptions.Type}'. Expected '{EngineOptions.KingsrowDllType}'.");
        }

        return new KingsrowEngineAdapter(
            workerId,
            engineOptions,
            provisioner,
            loggerFactory.CreateLogger<KingsrowEngineAdapter>());
    }
}
