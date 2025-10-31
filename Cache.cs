using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lib.Cache.Net8
{
    // Interfaces
    public interface ICacheService
    {
        /// <summary>
        /// Récupère un élément depuis le cache en utilisant la clé spécifiée.
        /// Retourne la valeur par défaut du type T si l'élément n'est pas trouvé.
        /// </summary>
        /// <typeparam name="T">Type de l'objet à récupérer</typeparam>
        /// <param name="key">Clé unique identifiant l'élément dans le cache</param>
        /// <returns>L'élément en cache ou la valeur par défaut si non trouvé</returns>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// Stocke un seul élément dans le cache. L'élément doit implémenter ICacheItem
        /// pour fournir automatiquement sa clé de cache via la propriété CacheKey.
        /// </summary>
        /// <typeparam name="T">Type de l'objet à stocker, doit implémenter ICacheItem</typeparam>
        /// <param name="item">Élément à stocker dans le cache</param>
        /// <param name="expirationMinutes">Durée d'expiration en minutes (30 par défaut)</param>
        Task SetAsync<T>(T item, int expirationMinutes = 30) where T : ICacheItem;

        /// <summary>
        /// Stocke une collection d'éléments dans le cache. Chaque élément est décomposé
        /// et stocké individuellement avec sa propre clé basée sur CacheKey.
        /// Cette méthode garantit que chaque élément est accessible séparément.
        /// </summary>
        /// <typeparam name="T">Type des objets à stocker, doivent implémenter ICacheItem</typeparam>
        /// <param name="items">Collection d'éléments à stocker</param>
        /// <param name="expirationMinutes">Durée d'expiration en minutes (30 par défaut)</param>
        Task SetAsync<T>(IEnumerable<T> items, int expirationMinutes = 30) where T : ICacheItem;

        /// <summary>
        /// Supprime un élément du cache en utilisant sa clé.
        /// Si l'élément n'existe pas, aucune erreur n'est générée.
        /// </summary>
        /// <param name="key">Clé de l'élément à supprimer</param>
        Task RemoveAsync(string key);

        /// <summary>
        /// Vérifie si un élément existe dans le cache sans le récupérer.
        /// Utile pour vérifier la présence d'un élément sans consommer de ressources de désérialisation.
        /// </summary>
        /// <param name="key">Clé de l'élément à vérifier</param>
        /// <returns>True si l'élément existe, False sinon</returns>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// Récupère plusieurs éléments du cache en une seule opération.
        /// Les éléments non trouvés sont ignorés (non inclus dans le résultat).
        /// </summary>
        /// <typeparam name="T">Type des objets à récupérer</typeparam>
        /// <param name="keys">Collection de clés des éléments à récupérer</param>
        /// <returns>Collection des éléments trouvés dans le cache</returns>
        Task<IEnumerable<T>> GetManyAsync<T>(IEnumerable<string> keys);

        /// <summary>
        /// Récupère un élément depuis le cache ou le crée s'il n'existe pas.
        /// Pattern thread-safe garantissant une seule exécution de la factory en cas d'accès concurrent.
        /// </summary>
        /// <typeparam name="T">Type de l'objet à récupérer/créer</typeparam>
        /// <param name="key">Clé unique identifiant l'élément dans le cache</param>
        /// <param name="factory">Fonction factory pour créer l'élément si non présent dans le cache</param>
        /// <param name="expirationMinutes">Durée d'expiration en minutes (30 par défaut)</param>
        /// <returns>L'élément depuis le cache ou nouvellement créé</returns>
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, int expirationMinutes = 30);
    }

    /// <summary>
    /// Interface que doivent implémenter les objets stockables dans le cache.
    /// Fournit la clé unique utilisée pour identifier l'objet dans le cache.
    /// </summary>
    public interface ICacheItem
    {
        /// <summary>
        /// Clé unique identifiant cet élément dans le cache.
        /// Doit être unique pour chaque instance distincte.
        /// </summary>
        string CacheKey { get; }
    }

    // Implémentation concrète
    /// <summary>
    /// Service de cache utilisant IMemoryCache pour le stockage en mémoire.
    /// Offre des fonctionnalités de cache avec expiration, gestion des collections
    /// et accès thread-safe via sémaphore.
    /// </summary>
    public class CacheService(IMemoryCache memoryCache, ILogger<CacheService> logger) : ICacheService, IDisposable
    {
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly ILogger<CacheService> _logger = logger;
        private bool _disposed;

        /// <summary>
        /// Sémaphore pour garantir l'accès thread-safe aux opérations d'écriture.
        /// Empêche les race conditions lors des mises à jour concurrentes du cache.
        /// </summary>
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>
        /// Préfixe ajouté à toutes les clés de cache pour éviter les collisions
        /// avec d'autres utilisations possibles de IMemoryCache.
        /// </summary>
        private const string CachePrefix = "__cache__";

        public Task<T?> GetAsync<T>(string key)
        {
            try
            {
                // Construction de la clé finale avec le préfixe pour l'isolation
                string cacheKey = BuildCacheKey(key);

                // Tentative de récupération depuis le cache mémoire
                if (_memoryCache.TryGetValue(cacheKey, out T cachedItem))
                {
                    _logger.LogDebug("Cache hit for key: {Key}", cacheKey);
                    return Task.FromResult(cachedItem);
                }

                // Log en cas d'échec de récupération (cache miss)
                _logger.LogDebug("Cache miss for key: {Key}", cacheKey);
                return Task.FromResult<T?>(default);
            }
            catch (Exception ex)
            {
                // Gestion robuste des erreurs : log l'erreur mais ne propage pas l'exception
                // pour éviter de faire échouer l'application en cas de problème de cache
                _logger.LogError(ex, "Error retrieving item from cache with key: {Key}", key);
                return Task.FromResult<T?>(default);
            }
        }

        public async Task SetAsync<T>(T item, int expirationMinutes = 30) where T : ICacheItem
        {
            // Validation des paramètres d'entrée
            ArgumentNullException.ThrowIfNull(item);

            // Acquisition du verrou pour garantir l'exclusivité d'accès
            await _semaphore.WaitAsync();
            try
            {
                // Construction de la clé à partir de la propriété CacheKey de l'objet
                string cacheKey = BuildCacheKey(item.CacheKey);

                // Configuration des options de cache : expiration glissante et taille
                MemoryCacheEntryOptions cacheOptions = new()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(expirationMinutes),
                    Size = 1 // Taille unitaire pour le tracking de mémoire
                };

                // Stockage effectif dans le cache mémoire
                _ = _memoryCache.Set(cacheKey, item, cacheOptions);
                _logger.LogDebug("Item cached successfully with key: {Key}", cacheKey);
            }
            finally
            {
                // Libération du verrou dans un bloc finally pour garantir l'exécution
                // même en cas d'exception
                _ = _semaphore.Release();
            }
        }

        public async Task SetAsync<T>(IEnumerable<T> items, int expirationMinutes = 30) where T : ICacheItem
        {
            // Validation des paramètres d'entrée
            ArgumentNullException.ThrowIfNull(items);

            // DÉCOMPOSITION DE LA COLLECTION : chaque élément est stocké individuellement
            // Création d'une tâche pour chaque élément de la collection
            IEnumerable<Task> tasks = items.Select(item => SetAsync(item, expirationMinutes));

            // Exécution parallèle de toutes les opérations de stockage
            await Task.WhenAll(tasks);

            // Log du nombre total d'éléments stockés
            _logger.LogDebug("Cached {Count} items individually", items.Count());
        }

        public Task RemoveAsync(string key)
        {
            // Construction de la clé et suppression directe du cache
            string cacheKey = BuildCacheKey(key);
            _memoryCache.Remove(cacheKey);
            _logger.LogDebug("Item removed from cache with key: {Key}", cacheKey);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            // Vérification de l'existence sans récupération de la valeur
            // Méthode performante car elle évite la désérialisation
            string cacheKey = BuildCacheKey(key);
            bool exists = _memoryCache.TryGetValue(cacheKey, out _);
            return Task.FromResult(exists);
        }

        public async Task<IEnumerable<T>> GetManyAsync<T>(IEnumerable<string> keys)
        {
            // Création d'une tâche de récupération pour chaque clé
            IEnumerable<Task<T?>> tasks = keys.Select(async key =>
            {
                T? item = await GetAsync<T>(key);
                return item;
            });

            // Exécution parallèle de toutes les récupérations
            T?[] items = await Task.WhenAll(tasks);

            // Filtrage des résultats null et retour de la collection typée
            return items.Where(item => item is not null)!;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, int expirationMinutes = 30)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(factory);

            string cacheKey = BuildCacheKey(key);

            try
            {
                // Tentative de récupération depuis le cache
                if (_memoryCache.TryGetValue(cacheKey, out T cachedItem))
                {
                    _logger.LogDebug("Cache hit for key: {Key}", cacheKey);
                    return cachedItem!;
                }

                _logger.LogDebug("Cache miss for key: {Key}. Creating new item...", cacheKey);

                // Acquisition du verrou pour éviter le cache stampede
                await _semaphore.WaitAsync();
                try
                {
                    // Double vérification après acquisition du verrou
                    if (_memoryCache.TryGetValue(cacheKey, out cachedItem))
                    {
                        _logger.LogDebug("Cache hit after lock acquisition for key: {Key}", cacheKey);
                        return cachedItem!;
                    }

                    // Exécution de la factory pour créer l'élément
                    T newItem = await factory().ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Factory function returned null");

                    // Configuration des options de cache
                    MemoryCacheEntryOptions cacheOptions = new()
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(expirationMinutes),
                        Size = 1
                    };

                    // Stockage dans le cache
                    _ = _memoryCache.Set(cacheKey, newItem, cacheOptions);

                    _logger.LogInformation("Item created and cached successfully with key: {Key}", cacheKey);
                    return newItem;
                }
                finally
                {
                    _ = _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetOrCreateAsync for key: {Key}", key);
                throw new InvalidOperationException($"Failed to get or create cache item for key '{key}'", ex);
            }
        }

        /// <summary>
        /// Construit une clé de cache complète en ajoutant le préfixe à la clé fournie.
        /// Cette méthode garantit l'isolation des clés dans le cache partagé.
        /// </summary>
        /// <param name="key">Clé originale de l'élément</param>
        /// <returns>Clé complète avec préfixe pour le cache</returns>
        private static string BuildCacheKey(string key)
        {
            return $"{CachePrefix}:{key}";
        }

        /// <summary>
        /// Libère les ressources managées et non-managées
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Implémentation protégée du pattern Dispose
        /// </summary>
        /// <param name="disposing">True pour libérer les ressources managées et non-managées, False pour libérer seulement les non-managées</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // Libère les ressources managées
                _semaphore?.Dispose();
                _logger.LogDebug("CacheService resources disposed");
            }

            _disposed = true;
        }

        /// <summary>
        /// Finaliseur pour libérer les ressources non-managées en cas d'oubli de Dispose
        /// </summary>
        ~CacheService()
        {
            Dispose(false);
        }
    }

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
            _ = services.AddMemoryCache((option) =>
            {
                option.SizeLimit = 1024; // Limite maximale d'items en cache
                option.CompactionPercentage = 0.2; // Pourcentage de compaction quand la limite est atteinte
            });

            // Enregistrement du service de cache avec durée de vie Scoped
            // (une instance par requête dans une application web)
            _ = services.AddScoped<ICacheService, CacheService>();
            return services;
        }

        /// <summary>
        /// Enregistre le service de cache avec une configuration personnalisée.
        /// Permet de personnaliser les options de IMemoryCache selon les besoins de l'application.
        /// </summary>
        /// <param name="services">Collection des services de l'application</param>
        /// <param name="configure">Action de configuration des options du cache mémoire</param>
        /// <returns>Collection des services pour le chaînage</returns>
        public static IServiceCollection AddCacheService(this IServiceCollection services, Action<MemoryCacheOptions> configure)
        {
            // Configuration personnalisée du cache mémoire
            _ = services.AddMemoryCache(configure);

            // Enregistrement du service de cache
            _ = services.AddScoped<ICacheService, CacheService>();

            return services;
        }
    }
}