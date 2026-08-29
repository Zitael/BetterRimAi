using System.Collections.Generic;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// Defines the protected base envelope used by threat-aware movement.
    ///
    /// A cell is safe when it is painted Home OR when it is an unpainted pocket completely
    /// enclosed by Home cells. This avoids treating tiny holes in the Home-area paint as outdoor
    /// space. We determine those pockets by flood-filling non-Home cells from the map edge: any
    /// non-Home cell the edge cannot reach without crossing Home belongs to the protected envelope.
    /// </summary>
    internal static class ThreatAwareHomeSafety
    {
        private const int CacheTicks = 1200;

        private sealed class CacheEntry
        {
            public int tick = -999999;
            public int width;
            public int height;
            public bool[] exteriorNonHome;
        }

        private static readonly Dictionary<int, CacheEntry> CacheByMap = new Dictionary<int, CacheEntry>();
        private static readonly Queue<int> FloodQueue = new Queue<int>(4096);

        public static bool IsSafeCell(Map map, Area_Home home, IntVec3 cell)
        {
            if (map == null || home == null || !cell.IsValid || !cell.InBounds(map))
                return false;

            if (home[cell])
                return true;

            CacheEntry cache = GetCache(map, home);
            int index = cell.z * cache.width + cell.x;
            return index >= 0 && index < cache.exteriorNonHome.Length && !cache.exteriorNonHome[index];
        }

        private static CacheEntry GetCache(Map map, Area_Home home)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!CacheByMap.TryGetValue(map.uniqueID, out CacheEntry cache))
            {
                cache = new CacheEntry();
                CacheByMap[map.uniqueID] = cache;
            }

            int width = map.Size.x;
            int height = map.Size.z;
            int cellCount = width * height;
            bool sizeChanged = cache.width != width || cache.height != height || cache.exteriorNonHome == null || cache.exteriorNonHome.Length != cellCount;
            if (!sizeChanged && tick - cache.tick < CacheTicks)
                return cache;

            cache.tick = tick;
            cache.width = width;
            cache.height = height;
            if (cache.exteriorNonHome == null || cache.exteriorNonHome.Length != cellCount)
                cache.exteriorNonHome = new bool[cellCount];
            else
                System.Array.Clear(cache.exteriorNonHome, 0, cache.exteriorNonHome.Length);

            FloodQueue.Clear();

            for (int x = 0; x < width; x++)
            {
                TrySeed(map, home, cache, x, 0);
                if (height > 1) TrySeed(map, home, cache, x, height - 1);
            }
            for (int z = 1; z < height - 1; z++)
            {
                TrySeed(map, home, cache, 0, z);
                if (width > 1) TrySeed(map, home, cache, width - 1, z);
            }

            while (FloodQueue.Count > 0)
            {
                int index = FloodQueue.Dequeue();
                int x = index % width;
                int z = index / width;
                if (x > 0) TrySeed(map, home, cache, x - 1, z);
                if (x + 1 < width) TrySeed(map, home, cache, x + 1, z);
                if (z > 0) TrySeed(map, home, cache, x, z - 1);
                if (z + 1 < height) TrySeed(map, home, cache, x, z + 1);
            }

            return cache;
        }

        private static void TrySeed(Map map, Area_Home home, CacheEntry cache, int x, int z)
        {
            int index = z * cache.width + x;
            if (cache.exteriorNonHome[index])
                return;

            IntVec3 cell = new IntVec3(x, 0, z);
            if (home[cell])
                return;

            cache.exteriorNonHome[index] = true;
            FloodQueue.Enqueue(index);
        }
    }
}
