using System.Collections.Generic;

namespace Jellyfin.Plugin.OppaiDex
{
    public static class DictionaryExtensions
    {
        public static T? GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value;
            }

            return default;
        }
    }
}
