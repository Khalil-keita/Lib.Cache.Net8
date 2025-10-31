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
- ✅ **Logging intégré** avec `ILogger`
- ✅ **Gestion de mémoire** avec pattern `IDisposable`
- ✅ **Support des types simples et complexes**

## 1. Configuration
```
// Program.cs
using Lib.Cache.Net8;

var builder = WebApplication.CreateBuilder(args);

// Configuration minimale
builder.Services.AddCacheService();

var app = builder.Build();
```
