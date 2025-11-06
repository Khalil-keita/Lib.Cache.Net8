using System.Collections.Concurrent;
using Lib.Cache.Net8.Src.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Lib.Cache.Net8.Src;

// Implémentation concrète
/// <summary>
/// Service de cache utilisant IMemoryCache pour le stockage en mémoire.
/// Offre des fonctionnalités de cache avec expiration, gestion des collections
/// et accès thread-safe via sémaphore.
/// </summary>
public sealed class Cache(IMemoryCache memoryCache) : ICacheable, IDisposable
{
    private readonly IMemoryCache _memoryCache = memoryCache;
    private bool _disposed;
    private const int DefaultExpirationMinutes = 30;

    private static readonly ConcurrentDictionary<string, IEnumerable<string>> _cacheKeys = new();

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

    #region Méthodes avec contraintes ICacheItem

    public async ValueTask<T?> GetAsync<T>(string key, Func<Task<T>>? resolver = null) where T : ICacheItem
    {
        try
        {
            // Construction de la clé finale avec le préfixe pour l'isolation
            string cacheKey = BuildCacheKey(key);

            // Tentative de récupération depuis le cache mémoire
            if (_memoryCache.TryGetValue(cacheKey, out T? cachedItem))
            {
                return cachedItem;
            }

            // Tentative de récupération depuis le resolver
            if (resolver != null)
            {
                T? item = await resolver();
                await SetAsync(item);
                return item;
            }

            return default;
        }
        catch (Exception)
        {
            // Gestion robuste des erreurs : log l'erreur mais ne propage pas l'exception
            // pour éviter de faire échouer l'application en cas de problème de cache
            return default;
        }
    }

    public async Task SetAsync<T>(T item, int expirationMinutes = DefaultExpirationMinutes) where T : ICacheItem
    {
        // Validation des paramètres d'entrée
        ArgumentNullException.ThrowIfNull(item);

        // Acquisition du verrou pour garantir l'exclusivité d'accès
        await _semaphore.WaitAsync();
        try
        {
            // Construction de la clé à partir de la propriété CacheKey de l'objet
            string cacheKey = BuildCacheKey(item.CacheKey);

            if (!await ExistsAsync(cacheKey))
            {
                // Configuration des options de cache : expiration glissante et taille
                MemoryCacheEntryOptions cacheOptions = new()
                {
                    SlidingExpiration = TimeSpan.FromMinutes(expirationMinutes),
                    Size = 1,
                    // Éviter la compaction trop rapide
                    Priority = CacheItemPriority.Normal
                };

                // Stockage effectif dans le cache mémoire
                _ = _memoryCache.Set(cacheKey, item, cacheOptions);
            }
        }
        finally
        {
            // Libération du verrou dans un bloc finally pour garantir l'exécution
            // même en cas d'exception
            _ = _semaphore.Release();
        }
    }

    public async Task SetAsync<T>(IEnumerable<T> items, int expirationMinutes = DefaultExpirationMinutes) where T : ICacheItem
    {
        // Validation des paramètres d'entrée
        ArgumentNullException.ThrowIfNull(items);

        // Filtrer les éléments null
        List<T> validItems = [.. items.Where(item => item != null)];

        if (validItems.Count == 0)
        {
            return;
        }

        // DÉCOMPOSITION DE LA COLLECTION : chaque élément est stocké individuellement
        // Création d'une tâche pour chaque élément de la collection
        IEnumerable<Task> tasks = validItems.Select(item => SetAsync(item, expirationMinutes));

        // Exécution parallèle de toutes les opérations de stockage
        await Task.WhenAll(tasks);
    }

    public async ValueTask<IEnumerable<T>> GetManyAsync<T>(IEnumerable<string> keys, Func<IEnumerable<string>, Task<IEnumerable<T>>>? resolver = null) where T : ICacheItem
    {
        List<string> cacheKeys = [.. keys.Select(BuildCacheKey)];
        List<T> foundItems = [];
        List<string> missingKeys = [];

        // 1. Récupération depuis le cache
        foreach (string cacheKey in cacheKeys)
        {
            if (_memoryCache.TryGetValue(cacheKey, out T? cachedItem) && cachedItem != null)
            {
                foundItems.Add(cachedItem);
            }
            else
            {
                // Extraire la clé originale sans le préfixe
                string originalKey = cacheKey[(CachePrefix.Length + 1)..];
                missingKeys.Add(originalKey);
            }
        }

        // 2. Résolution des éléments manquants
        if (missingKeys.Count > 0 && resolver != null)
        {
            IEnumerable<T> resolvedItems = await resolver(missingKeys);
            List<T> validResolvedItems = resolvedItems?.Where(item => item != null).ToList() ?? [];

            if (validResolvedItems.Count != 0)
            {
                // Stocker les nouveaux éléments
                await SetAsync(validResolvedItems);
                foundItems.AddRange(validResolvedItems);
            }
        }

        return foundItems;
    }

    public async Task<IEnumerable<T?>> GetOrCreateAsync<T>(string key, Func<Task<IEnumerable<T?>>> factory, int expirationMinutes = 30) where T : ICacheItem
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        string cacheKey = BuildCacheKey(key);

        // 1. Première tentative sans verrou
        if (_cacheKeys.TryGetValue(cacheKey, out IEnumerable<string>? keys))
        {
            IEnumerable<T?> items = await GetManyAsync<T>(keys, null);
            if (items.Any()) // Vérifier que nous avons des données
            {
                return items;
            }
        }

        try
        {
            // 2. Acquisition du verrou pour toute la durée critique
            await _semaphore.WaitAsync();
            try
            {
                // Double vérification après acquisition du verrou
                if (_cacheKeys.TryGetValue(cacheKey, out keys))
                {
                    IEnumerable<T?> items = await GetManyAsync<T>(keys, null);
                    if (items.Any())
                    {
                        return items;
                    }
                }
            }
            finally
            {
                _ = _semaphore.Release();
            }

            // 3. Exécution de la factory sous protection du verrou
            IEnumerable<T?> newItems = await factory();

            if (newItems == null || !newItems.Any())
            {
                // Stocker une collection vide pour éviter les appels répétés
                IEnumerable<T?> emptyItems = [];
                CacheEmptyCollection(cacheKey, expirationMinutes);
                return emptyItems;
            }

            // 4. Stockage des nouveaux éléments
            List<T> validItems = [.. newItems.Where(item => item != null)];
            if (validItems.Count != 0)
            {
                await SetAsync(validItems, expirationMinutes);

                // Mise à jour du dictionnaire des clés
                List<string> itemKeys = [.. validItems.Select(item => item!.CacheKey)];
                _ = _cacheKeys.AddOrUpdate(cacheKey, itemKeys, (k, v) => itemKeys);
            }

            return newItems;
        }
        catch (Exception)
        {
            // Gestion robuste des erreurs
            return default!;
        }
    }

    #endregion Méthodes avec contraintes ICacheItem

    #region Méthodes sans contrainte ICacheItem

    /// <summary>
    /// Récupère un élément simple depuis le cache sans contrainte de type.
    /// Supporte tous les types, y compris les types primitifs et les objets simples.
    /// </summary>
    public async ValueTask<T?> GetSimpleAsync<T>(string key, Func<Task<T>>? resolver = null)
    {
        try
        {
            string cacheKey = BuildCacheKey(key);

            // Tentative de récupération depuis le cache mémoire
            if (_memoryCache.TryGetValue(cacheKey, out T? cachedItem))
            {
                return cachedItem;
            }

            // Tentative de récupération depuis le resolver
            if (resolver != null)
            {
                T? item = await resolver();
                if (item != null)
                {
                    await SetSimpleAsync(key, item);
                }
                return item;
            }

            return default;
        }
        catch (Exception)
        {
            // Gestion robuste des erreurs
            return default;
        }
    }

    /// <summary>
    /// Stocke un élément simple dans le cache sans nécessité d'implémenter ICacheItem.
    /// Utilise une clé explicite fournie en paramètre.
    /// </summary>
    public async Task SetSimpleAsync<T>(string key, T item, int expirationMinutes = DefaultExpirationMinutes)
    {
        // Validation des paramètres d'entrée
        ArgumentException.ThrowIfNullOrEmpty(key);

        // Les valeurs null sont autorisées pour représenter l'absence de valeur
        if (item == null)
        {
            return;
        }

        // Acquisition du verrou pour garantir l'exclusivité d'accès
        await _semaphore.WaitAsync();
        try
        {
            string cacheKey = BuildCacheKey(key);

            // Configuration des options de cache
            MemoryCacheEntryOptions cacheOptions = new()
            {
                SlidingExpiration = TimeSpan.FromMinutes(expirationMinutes),
                Size = 1,
                Priority = CacheItemPriority.Normal
            };

            // Stockage effectif dans le cache mémoire
            _ = _memoryCache.Set(cacheKey, item, cacheOptions);
        }
        finally
        {
            // Libération du verrou
            _ = _semaphore.Release();
        }
    }

    /// <summary>
    /// Stocke une collection d'éléments simples sous une seule clé.
    /// La collection est stockée en tant qu'objet unique (List<T>).
    /// </summary>
    public async Task SetSimpleAsync<T>(string key, IEnumerable<T> items, int expirationMinutes = DefaultExpirationMinutes)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(items);

        // Convertir la collection en liste pour la stocker
        List<T> itemsList = [.. items];

        if (itemsList.Count == 0)
        {
            return;
        }

        // Stocker la collection complète sous une seule clé
        await SetSimpleAsync(key, itemsList, expirationMinutes);
    }

    #endregion Méthodes sans contrainte ICacheItem

    #region Implémentation des méthodes spécialisées

    /// <summary>
    /// Récupère une chaîne de caractères depuis le cache.
    /// Méthode spécialisée offrant une syntaxe plus naturelle pour le type string.
    /// </summary>
    public ValueTask<string?> GetStringAsync(string key, Func<Task<string>>? resolver = null)
    {
        return GetSimpleAsync(key, resolver);
    }

    /// <summary>
    /// Stocke une chaîne de caractères dans le cache.
    /// Méthode spécialisée pour une meilleure expérience utilisateur avec les strings.
    /// </summary>
    public Task SetStringAsync(string key, string value, int expirationMinutes = 30)
    {
        return SetSimpleAsync(key, value, expirationMinutes);
    }

    /// <summary>
    /// Récupère un entier depuis le cache.
    /// Méthode spécialisée pour le type int avec gestion des conversions.
    /// </summary>
    public async ValueTask<int?> GetIntAsync(string key, Func<Task<int>>? resolver = null)
    {
        try
        {
            // Essayer de récupérer comme int directement
            int? result = await GetSimpleAsync<int?>(key);
            if (result.HasValue)
            {
                return result.Value;
            }

            // Essayer de récupérer comme string et convertir
            string? stringResult = await GetStringAsync(key);
            if (stringResult != null && int.TryParse(stringResult, out int intValue))
            {
                await SetIntAsync(key, intValue);
                return intValue;
            }

            // Utiliser le resolver si fourni
            if (resolver != null)
            {
                int value = await resolver();
                await SetIntAsync(key, value);
                return value;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Stocke un entier dans le cache.
    /// </summary>
    public Task SetIntAsync(string key, int value, int expirationMinutes = 30)
    {
        return SetSimpleAsync(key, value, expirationMinutes);
    }

    #endregion Implémentation des méthodes spécialisées

    #region Méthodes communes

    public Task<bool> ExistsAsync(string key)
    {
        // Vérification de l'existence sans récupération de la valeur
        // Méthode performante car elle évite la désérialisation
        string cacheKey = BuildCacheKey(key);
        bool exists = _memoryCache.TryGetValue(cacheKey, out _);
        return Task.FromResult(exists);
    }

    public void Remove(string key)
    {
        // Construction de la clé et suppression directe du cache
        string cacheKey = BuildCacheKey(key);
        _memoryCache.Remove(cacheKey);
    }

    #endregion Méthodes communes

    #region Methodes privees utilitaires

    private void CacheEmptyCollection(string cacheKey, int expirationMinutes)
    {
        // Créer un marqueur pour les collections vides
        EmptyCacheMarker emptyMarker = new();
        MemoryCacheEntryOptions cacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expirationMinutes),
            Size = 1
        };

        _ = _memoryCache.Set(cacheKey, emptyMarker, cacheOptions);
    }

    private sealed class EmptyCacheMarker : ICacheItem
    {
        public string CacheKey => "EMPTY_COLLECTION_MARKER";
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
    /// Implémentation protégée du pattern Dispose
    /// </summary>
    /// <param name="disposing">True pour libérer les ressources managées et non-managées, False pour libérer seulement les non-managées</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Libère les ressources managées
            _semaphore?.Dispose();
        }

        _disposed = true;
    }

    #endregion Methodes privees utilitaires

    #region Dispose

    /// <summary>
    /// Libère les ressources managées et non-managées
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finaliseur pour libérer les ressources non-managées en cas d'oubli de Dispose
    /// </summary>
    ~Cache()
    {
        Dispose(false);
    }

    #endregion Dispose
}