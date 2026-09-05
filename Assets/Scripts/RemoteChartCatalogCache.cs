using System;
using System.IO;
using Newtonsoft.Json;

namespace Gugarhythm
{
    public sealed class RemoteChartCatalogCache
    {
        const int CurrentSchemaVersion = 1;

        readonly string path;

        [Serializable]
        sealed class CatalogCacheFile
        {
            [JsonProperty("schemaVersion")] public int SchemaVersion;
            [JsonProperty("cachedAtUnixMilliseconds")] public long CachedAtUnixMilliseconds;
            [JsonProperty("catalog")] public RemoteChartCatalog Catalog;
        }

        public RemoteChartCatalogCache(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Cache path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public bool TryLoad(out RemoteChartCatalog catalog)
        {
            catalog = null;
            if (!File.Exists(path)) return false;

            try
            {
                var cache = JsonConvert.DeserializeObject<CatalogCacheFile>(File.ReadAllText(path));
                if (cache == null || cache.SchemaVersion != CurrentSchemaVersion ||
                    cache.CachedAtUnixMilliseconds < 0 || cache.Catalog == null || cache.Catalog.Charts == null)
                    return false;

                var catalogJson = JsonConvert.SerializeObject(cache.Catalog);
                if (!RemoteChartCatalogCodec.TryParse(catalogJson, out var parsed, out _)) return false;
                parsed.CachedAtUnixMilliseconds = cache.CachedAtUnixMilliseconds;
                catalog = parsed;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Save(RemoteChartCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.CachedAtUnixMilliseconds < 0)
                throw new ArgumentException("Cache timestamp cannot be negative.", nameof(catalog));
            if (ContainsPrivateChart(catalog))
            {
#if UNITY_EDITOR
                throw new InvalidOperationException(
                    "RemoteChartCatalogCache.Save must never persist a private-scope catalog to disk.");
#else
                return;
#endif
            }
            if (!RemoteChartCatalogCodec.TryParse(JsonConvert.SerializeObject(catalog), out _, out var error))
                throw new ArgumentException(error, nameof(catalog));

            var cache = new CatalogCacheFile
            {
                SchemaVersion = CurrentSchemaVersion,
                CachedAtUnixMilliseconds = catalog.CachedAtUnixMilliseconds,
                Catalog = catalog,
            };
            ChartVaultAtomicJsonFile.Write(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
        }

        static bool ContainsPrivateChart(RemoteChartCatalog catalog)
        {
            if (catalog.Charts == null) return false;
            foreach (var chart in catalog.Charts)
                if (chart != null && chart.IsPrivate) return true;
            return false;
        }
    }
}
