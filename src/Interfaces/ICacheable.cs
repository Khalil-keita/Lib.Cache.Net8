namespace Lib.Cache.Net8.Src.Interfaces;

public interface ICacheable
{
    /// <summary>
    /// Récupère un élément depuis le cache en utilisant la clé spécifiée.
    /// Retourne la valeur par défaut du type T si l'élément n'est pas trouvé.
    /// </summary>
    /// <typeparam name="T">Type de l'objet à récupérer</typeparam>
    /// <param name="key">Clé unique identifiant l'élément dans le cache</param>
    /// <param name="resolver">Fonction de resolution si l'élément n'est pas trouvé</param>
    /// <returns>L'élément en cache ou la valeur par défaut si non trouvé</returns>
    ValueTask<T?> GetAsync<T>(string key, Func<Task<T>>? resolver = null) where T : ICacheItem;

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
    void Remove(string key);

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
    ValueTask<IEnumerable<T>> GetManyAsync<T>(IEnumerable<string> keys, Func<IEnumerable<string>, Task<IEnumerable<T>>>? resolver = null) where T : ICacheItem;

    /// <summary>
    /// Récupère des éléments depuis le cache ou les crées s'ils n'existent pas.
    /// Pattern thread-safe garantissant une seule exécution de la factory en cas d'accès concurrent.
    /// </summary>
    /// <typeparam name="T">Type de l'objet à récupérer/créer</typeparam>
    /// <param name="key">Clé unique identifiant l'élément dans le cache</param>
    /// <param name="factory">Fonction factory pour créer l'élément si non présent dans le cache</param>
    /// <param name="expirationMinutes">Durée d'expiration en minutes (30 par défaut)</param>
    /// <returns>L'élément depuis le cache ou nouvellement créé</returns>
    Task<IEnumerable<T?>> GetOrCreateAsync<T>(string key, Func<Task<IEnumerable<T?>>> factory, int expirationMinutes = 30) where T : ICacheItem;
}
