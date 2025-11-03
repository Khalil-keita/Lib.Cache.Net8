using Lib.Cache.Net8.Src.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lib.Cache.Net8.Src;


// Configuration et Extension Methods
/// <summary>
/// Méthodes d'extension pour l'enregistrement du service de cache dans le conteneur DI.
/// Simplifie la configuration et l'utilisation du service.
/// </summary>
public static class CacheServiceExtensions
{
    /// <summary>
    /// Enregistre le service de cache avec une configuration par défaut.
    /// Configure IMemoryCache avec une limite de taille et un pourcentage de compaction.
    /// </summary>
    /// <param name="services">Collection des services de l'application</param>
    /// <returns>Collection des services pour le chaînage</returns>
    public static IServiceCollection AddCacheService(this IServiceCollection services)
    {
        // Configuration par défaut du cache mémoire
        return services.AddMemoryCache((option) =>
        {
            option.SizeLimit = 1024; // Limite maximale d'items en cache
            option.CompactionPercentage = 0.2; // Pourcentage de compaction quand la limite est atteinte
        })

        // Enregistrement du service de cache avec durée de vie Scoped
        // (une instance par requête dans une application web)
        .AddScoped<ICacheable, Cache>();
    }

    /// <summary>
    /// Enregistre le service de cache avec une configuration personnalisée.
    /// Permet de personnaliser les options de IMemoryCache selon les besoins de l'application.
    /// </summary>
    /// <param name="services">Collection des services de l'application</param>
    /// <param name="configure">Action de configuration des options du cache mémoire</param>
    /// <returns>Collection des services pour le chaînage</returns>
    public static IServiceCollection AddCacheService(
        this IServiceCollection services, Action<MemoryCacheOptions> configure
    )
    {
        // Configuration personnalisée du cache mémoire
        return services.AddMemoryCache(configure)

        // Enregistrement du service de cache
        .AddScoped<ICacheable, Cache>();
    }
}