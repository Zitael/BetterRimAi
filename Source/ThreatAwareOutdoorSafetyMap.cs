using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    // Shared lazy map snapshot. Mobile danger expands only through walkable cells;
    // turret danger follows its actual firing verb. Warm candidates use byte lookups.
    internal static class ThreatAwareOutdoorSafetyMap
    {
        private const int RefreshTicks = 180;
        private sealed class Snapshot
        {
            internal int tick = -999999;
            internal int width, height;
            internal byte[] danger, reachable;
            internal int[] visited;
            internal int visitGeneration;
            internal int[] queue;
            internal readonly List<ThreatAwareThreat> threats = new List<ThreatAwareThreat>();
            // Actual unsafe routes discovered by the last-resort guard. These are exact
            // destination cells, never JobDefs, and persist only while their danger cell is hot.
            internal readonly Dictionary<int, int> observedRoutes = new Dictionary<int, int>();
            internal readonly List<int> staleRoutes = new List<int>();
            internal bool hasDanger;
        }
        private static ConditionalWeakTable<Map, Snapshot> snapshots = new ConditionalWeakTable<Map, Snapshot>();
        internal static void Reset() => snapshots = new ConditionalWeakTable<Map, Snapshot>();
        internal static void Invalidate(Map map) { if (map != null) snapshots.Remove(map); }
        internal static void RefreshForDeparture(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null || !snapshots.TryGetValue(map, out Snapshot snapshot)) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < snapshot.tick || tick - snapshot.tick >= 10) snapshot.tick = -999999;
        }

        internal static bool Unsafe(Pawn pawn, LocalTargetInfo target, out Thing threat)
        {
            threat = null;
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !target.IsValid || !target.Cell.InBounds(map)
                || map.terrainGrid == null) return false;
            Snapshot snapshot = Get(pawn, map, home);
            if (!snapshot.hasDanger) return false;
            int index = Index(snapshot, target.Cell);
            if (snapshot.observedRoutes.ContainsKey(index))
            {
                threat = snapshot.threats.Count == 0 ? null : snapshot.threats[0].Source;
                return true;
            }
            if (snapshot.reachable[index] != 0) return false;
            threat = snapshot.threats.Count == 0 ? null : snapshot.threats[0].Source;
            return true;
        }

        internal static bool ThreatensCell(Pawn pawn, IntVec3 cell, out Thing threat)
        {
            threat = null;
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !cell.InBounds(map) || map.terrainGrid == null) return false;
            Snapshot snapshot = Get(pawn, map, home);
            if (!snapshot.hasDanger || snapshot.danger[Index(snapshot, cell)] == 0) return false;
            threat = snapshot.threats.Count == 0 ? null : snapshot.threats[0].Source;
            return true;
        }

        internal static void ObserveUnsafeRoute(Pawn pawn, LocalTargetInfo destination, IntVec3 dangerCell)
        {
            Map map = pawn?.Map;
            if (map == null || !destination.IsValid || !destination.Cell.InBounds(map)
                || !dangerCell.InBounds(map)) return;
            Snapshot snapshot = snapshots.GetOrCreateValue(map);
            if (snapshot.width != map.Size.x || snapshot.height != map.Size.z) return;
            snapshot.observedRoutes[Index(snapshot, destination.Cell)] = Index(snapshot, dangerCell);
        }

        private static Snapshot Get(Pawn pawn, Map map, Area_Home home)
        {
            Snapshot snapshot = snapshots.GetOrCreateValue(map);
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (snapshot.tick == -999999 || snapshot.width != map.Size.x || snapshot.height != map.Size.z
                || tick < snapshot.tick || tick - snapshot.tick >= RefreshTicks)
            {
                try { Rebuild(pawn, map, home, tick, snapshot); }
                catch (Exception ex)
                {
                    snapshot.tick = tick;
                    snapshot.hasDanger = false;
                    Log.Warning("[BetterRimAI] Threat snapshot refresh failed: " + ex);
                }
            }
            return snapshot;
        }

        private static int Index(Snapshot s, IntVec3 c) => c.z * s.width + c.x;
        private static void Rebuild(Pawn pawn, Map map, Area_Home home, int tick, Snapshot s)
        {
            s.tick = tick;
            s.width = map.Size.x;
            s.height = map.Size.z;
            bool previouslyDangerous = s.hasDanger;
            s.threats.Clear();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn hostile = pawns[i];
                if (hostile == pawn || hostile.Dead || hostile.Downed || !hostile.Spawned
                    || !(hostile.HostileTo(pawn) || pawn.HostileTo(hostile) || hostile.HostileTo(pawn.Faction))
                    || !GenHostility.IsPotentialThreat(hostile)) continue;
                s.threats.Add(new ThreatAwareThreat(hostile));
            }
            if (map.attackTargetsCache != null)
                foreach (IAttackTarget target in map.attackTargetsCache.TargetsHostileToFaction(pawn.Faction))
                    if (ThreatAwareThreat.TryCreateStructure(pawn, target, out ThreatAwareThreat structure))
                        s.threats.Add(structure);
            s.hasDanger = s.threats.Count != 0;
            if (!s.hasDanger)
            {
                s.observedRoutes.Clear();
                if (previouslyDangerous)
                    ThreatAwareBlockDiagnostics.Once("restriction-cleared", pawn, null, null, false);
                return;
            }
            int count = s.width * s.height;
            if (s.danger == null || s.danger.Length != count)
            {
                s.danger = new byte[count]; s.reachable = new byte[count];
                s.visited = new int[count]; s.queue = new int[count];
                s.visitGeneration = 0;
            }
            else
            {
                Array.Clear(s.danger, 0, count);
                Array.Clear(s.reachable, 0, count);
            }
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            for (int i = 0; i < s.threats.Count; i++)
            {
                ThreatAwareThreat source = s.threats[i];
                if (source.IsRangedStructure) MarkFiringCells(map, s, source);
                else MarkConnectedMobileCells(map, s, source.Source.Position,
                    Math.Max(settings.routeThreatRadius, settings.homeExitThreatRadius));
            }
            int head = 0, tail = 0;
            for (int z = 0; z < s.height; z++)
                for (int x = 0; x < s.width; x++)
                {
                    var cell = new IntVec3(x, 0, z);
                    int index = z * s.width + x;
                    if (!ThreatAwareHomeSafety.IsSafeCell(map, home, cell) || !cell.Standable(map)) continue;
                    s.reachable[index] = 1;
                    s.queue[tail++] = index;
                }
            while (head < tail)
            {
                int index = s.queue[head++], x = index % s.width, z = index / s.width;
                if (x > 0) Visit(map, s, x - 1, z, ref tail);
                if (x + 1 < s.width) Visit(map, s, x + 1, z, ref tail);
                if (z > 0) Visit(map, s, x, z - 1, ref tail);
                if (z + 1 < s.height) Visit(map, s, x, z + 1, ref tail);
            }
            s.staleRoutes.Clear();
            foreach (KeyValuePair<int, int> route in s.observedRoutes)
                if (route.Key < 0 || route.Key >= count || route.Value < 0 || route.Value >= count || s.danger[route.Value] == 0)
                    s.staleRoutes.Add(route.Key);
            for (int i = 0; i < s.staleRoutes.Count; i++) s.observedRoutes.Remove(s.staleRoutes[i]);
        }

        private static void MarkConnectedMobileCells(Map map, Snapshot s, IntVec3 origin, float radius)
        {
            if (!origin.InBounds(map)) return;
            if (s.visitGeneration == int.MaxValue)
            {
                Array.Clear(s.visited, 0, s.visited.Length);
                s.visitGeneration = 0;
            }
            int generation = ++s.visitGeneration;
            int head = 0, tail = 0, start = Index(s, origin);
            s.visited[start] = generation;
            s.queue[tail++] = start;
            float squared = radius * radius;
            while (head < tail)
            {
                int index = s.queue[head++], x = index % s.width, z = index / s.width;
                int dx = x - origin.x, dz = z - origin.z;
                if (dx * dx + dz * dz > squared) continue;
                s.danger[index] = 1;
                if (x > 0) VisitMobile(map, s, x - 1, z, generation, ref tail);
                if (x + 1 < s.width) VisitMobile(map, s, x + 1, z, generation, ref tail);
                if (z > 0) VisitMobile(map, s, x, z - 1, generation, ref tail);
                if (z + 1 < s.height) VisitMobile(map, s, x, z + 1, generation, ref tail);
            }
        }
        private static void VisitMobile(Map map, Snapshot s, int x, int z, int generation, ref int tail)
        {
            int index = z * s.width + x;
            if (s.visited[index] == generation) return;
            s.visited[index] = generation;
            var cell = new IntVec3(x, 0, z);
            if (!cell.Standable(map)) return;
            Building_Door door = map.edificeGrid == null ? null : cell.GetEdifice(map) as Building_Door;
            if (door == null || door.Open) s.queue[tail++] = index;
        }
        private static void MarkFiringCells(Map map, Snapshot s, ThreatAwareThreat source)
        {
            int range = (int)Math.Ceiling(source.Range);
            IntVec3 origin = source.Source.Position;
            for (int z = Math.Max(0, origin.z - range); z <= Math.Min(s.height - 1, origin.z + range); z++)
                for (int x = Math.Max(0, origin.x - range); x <= Math.Min(s.width - 1, origin.x + range); x++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (source.ThreatensCell(map, cell, 0f)) s.danger[z * s.width + x] = 1;
                }
        }
        private static void Visit(Map map, Snapshot s, int x, int z, ref int tail)
        {
            int index = z * s.width + x;
            if (s.reachable[index] != 0 || s.danger[index] != 0 || !new IntVec3(x, 0, z).Standable(map)) return;
            s.reachable[index] = 1;
            s.queue[tail++] = index;
        }
    }
}
