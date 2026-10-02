using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// One map-level, event-driven danger snapshot for cheap pre-travel candidate rejection.
    /// Native path validation remains the authority if vanilla selects a different route.
    /// </summary>
    internal static class ThreatAwareOutdoorSafetyMap
    {
        private sealed class Snapshot
        {
            internal int tick = -999999;
            internal int width;
            internal int height;
            internal bool hasDanger;
            internal byte[] danger;
            internal byte[] reachable;
            internal int[] queue;
            internal int lastErrorTick = -999999;
        }
        private static ConditionalWeakTable<Map, Snapshot> snapshots = new ConditionalWeakTable<Map, Snapshot>();
        internal static void Reset() => snapshots = new ConditionalWeakTable<Map, Snapshot>();

        internal static bool Unsafe(Pawn pawn, LocalTargetInfo target)
        {
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !target.IsValid || !target.Cell.InBounds(map)
                || ThreatAwareHomeSafety.IsSafeCell(map, home, target.Cell)) return false;
            // Terrain data is absent only in headless selection fixtures.
            if (map.terrainGrid == null || map.mapPawns == null) return false;
            Snapshot snapshot = snapshots.GetOrCreateValue(map);
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < snapshot.tick || tick - snapshot.tick >= ThreatAwareOutdoorWorkPatch.HostileCacheTicks
                || snapshot.width != map.Size.x || snapshot.height != map.Size.z)
            {
                try { Rebuild(pawn, map, home, tick, snapshot); }
                catch (Exception ex)
                {
                    snapshot.tick = tick;
                    snapshot.hasDanger = false;
                    if (tick - snapshot.lastErrorTick >= 600)
                    {
                        snapshot.lastErrorTick = tick;
                        Log.Warning("[BetterRimAI] Could not refresh outdoor danger snapshot: " + ex);
                    }
                    return false;
                }
            }
            if (!snapshot.hasDanger) return false;
            int index = target.Cell.z * snapshot.width + target.Cell.x;
            if (snapshot.reachable[index] != 0) return false;
            if (target.HasThing)
            {
                CellRect rect = GenAdj.OccupiedRect(target.Thing);
                for (int z = rect.minZ - 1; z <= rect.maxZ + 1; z++)
                    for (int x = rect.minX - 1; x <= rect.maxX + 1; x++)
                        if (x >= 0 && z >= 0 && x < snapshot.width && z < snapshot.height
                            && snapshot.reachable[z * snapshot.width + x] != 0) return false;
            }
            return true;
        }

        private static void Rebuild(Pawn pawn, Map map, Area_Home home, int tick, Snapshot snapshot)
        {
            snapshot.tick = tick;
            snapshot.width = map.Size.x;
            snapshot.height = map.Size.z;
            int count = snapshot.width * snapshot.height;
            if (snapshot.danger == null || snapshot.danger.Length != count)
            {
                snapshot.danger = new byte[count];
                snapshot.reachable = new byte[count];
                snapshot.queue = new int[count];
            }
            else
            {
                Array.Clear(snapshot.danger, 0, count);
                Array.Clear(snapshot.reachable, 0, count);
            }

            List<ThreatAwareThreat> threats = ThreatAwareOutdoorWorkPatch.GetRelevantHostilesCached(pawn, map, tick);
            snapshot.hasDanger = threats.Count != 0;
            if (!snapshot.hasDanger) return;
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            for (int i = 0; i < threats.Count; i++)
            {
                ThreatAwareThreat threat = threats[i];
                Thing source = threat.Source;
                float radius = threat.IsRangedStructure ? threat.Range : Math.Max(settings.routeThreatRadius, settings.homeExitThreatRadius);
                int range = (int)Math.Ceiling(radius);
                int minX = Math.Max(0, source.Position.x - range);
                int maxX = Math.Min(snapshot.width - 1, source.Position.x + range);
                int minZ = Math.Max(0, source.Position.z - range);
                int maxZ = Math.Min(snapshot.height - 1, source.Position.z + range);
                float squared = radius * radius;
                for (int z = minZ; z <= maxZ; z++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        int dx = x - source.Position.x;
                        int dz = z - source.Position.z;
                        if (dx * dx + dz * dz <= squared)
                            snapshot.danger[z * snapshot.width + x] = 1;
                    }
            }

            // Flood from protected, standable cells once per snapshot. A candidate then needs
            // only a single indexed lookup, with no hostile scan or pathfinder call.
            int head = 0;
            int tail = 0;
            for (int z = 0; z < snapshot.height; z++)
                for (int x = 0; x < snapshot.width; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    int index = z * snapshot.width + x;
                    if (!ThreatAwareHomeSafety.IsSafeCell(map, home, cell) || !cell.Standable(map)) continue;
                    snapshot.reachable[index] = 1;
                    snapshot.queue[tail++] = index;
                }
            while (head < tail)
            {
                int index = snapshot.queue[head++];
                int x = index % snapshot.width;
                int z = index / snapshot.width;
                if (x > 0) Visit(map, snapshot, x - 1, z, ref tail);
                if (x + 1 < snapshot.width) Visit(map, snapshot, x + 1, z, ref tail);
                if (z > 0) Visit(map, snapshot, x, z - 1, ref tail);
                if (z + 1 < snapshot.height) Visit(map, snapshot, x, z + 1, ref tail);
            }
        }

        private static void Visit(Map map, Snapshot snapshot, int x, int z, ref int tail)
        {
            int index = z * snapshot.width + x;
            if (snapshot.reachable[index] != 0 || snapshot.danger[index] != 0
                || !new IntVec3(x, 0, z).Standable(map)) return;
            snapshot.reachable[index] = 1;
            snapshot.queue[tail++] = index;
        }
    }
}
