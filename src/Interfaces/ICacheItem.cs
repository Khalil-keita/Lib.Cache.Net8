namespace Lib.Cache.Net8.Src.Interfaces;

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
