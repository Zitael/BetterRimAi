using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// The protected colony: painted Home plus unpainted pockets enclosed by Home
    /// (see <see cref="ThreatGeometry.BuildEnvelope"/>). One lazily rebuilt grid per map.
    /// </summary>
    internal static class ProtectedArea
    {
        internal const int DefaultMaxAgeTicks = 1200;

        private sealed class Cache
        {
            internal int tick = int.MinValue;
            internal int width, height;
            internal bool[] home, isProtected;
            internal int[] queue;
        }

        private static ConditionalWeakTable<Map, Cache> caches = new ConditionalWeakTable<Map, Cache>();

        internal static void Reset() => caches = new ConditionalWeakTable<Map, Cache>();

        internal static bool IsProtected(Map map, IntVec3 cell)
        {
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !cell.InBounds(map)) return false;
            if (home[cell]) return true;
            bool[] grid = Get(map, DefaultMaxAgeTicks);
            return grid != null && grid[map.cellIndices.CellToIndex(cell)];
        }

        /// <summary>Envelope grid indexed by map cell, or null when the map has no Home area.</summary>
        internal static bool[] Get(Map map, int maxAgeTicks)
        {
            Area_Home home = map?.areaManager?.Home;
            if (home == null) return null;
            Cache cache = caches.GetOrCreateValue(map);
            int tick = Find.TickManager?.TicksGame ?? 0;
            int width = map.Size.x, height = map.Size.z, count = width * height;
            bool fresh = cache.isProtected != null && cache.width == width && cache.height == height
                         && tick >= cache.tick && tick - cache.tick < maxAgeTicks;
            if (fresh) return cache.isProtected;

            if (cache.isProtected == null || cache.isProtected.Length != count)
            {
                cache.home = new bool[count];
                cache.isProtected = new bool[count];
                cache.queue = new int[count];
            }
            cache.width = width;
            cache.height = height;
            cache.tick = tick;
            for (int i = 0; i < count; i++) cache.home[i] = home[i];
            ThreatGeometry.BuildEnvelope(cache.home, width, height, cache.isProtected, cache.queue);
            return cache.isProtected;
        }
    }
}
