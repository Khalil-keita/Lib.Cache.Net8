# Lib.Cache.Net8

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-blue)

Une librairie de caching simple, performante et robuste pour .NET 8, basée sur `IMemoryCache` avec support avancé du stockage individuel des collections et gestion automatique de la mémoire.

## 🚀 Fonctionnalités

- ✅ **Cache mémoire haute performance** avec `IMemoryCache`
- ✅ **Décomposition automatique des collections** - chaque élément stocké individuellement
- ✅ **Interface fluide** et intuitive avec support async/await
- ✅ **Thread-safe** complet avec sémaphore intégré
- ✅ **Expiration configurable** par élément ou par lot
- ✅ **Support DI natif** (Dependency Injection)
- ✅ **Gestion de mémoire** avec pattern `IDisposable`
- ✅ **Support des types simples et complexes**

## 1. Configuration

```C#
// Program.cs
using Lib.Cache.Net8;

var builder = WebApplication.CreateBuilder(args);

// Configuration minimale
builder.Services.AddCacheService();

//Ou avec des options (Action<ImemoryCacheOptions>);
builder.Services.AddCacheService(op => {
    op.SizeLimit = 1024;
    op.CompactionPercentage = 0.2;
})

var app = builder.Build();
```

## 2. Créer un modèle

```C#
public class User : ICacheItem //Interface definie dans le service de cache
{
    public string Id { get; set; }
    public string Name { get; set; }
    
    public string CacheKey => $"user:{Id}";
}
```

## Utilisation de base

```C#
public class UserService
{
    private readonly ICacheService _cache;

    public UserService(ICacheService cache)
    {
        _cache = cache;
    }

    // Stocker un élément
    public async Task CacheUserAsync(User user)
    {
        await _cache.SetAsync(user, 60); // 60 minutes
    }

    // Récupérer un élément
    public async Task<User?> GetUserAsync(string userId)
    {
        return await _cache.GetAsync<User>($"user:{userId}");
    }
}
```
