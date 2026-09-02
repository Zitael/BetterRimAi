using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Threat-aware movement guard for player-controlled non-animal pawns.
    ///
    /// Performance rule: do not patch PatherTick. TryEnterNextPathCell runs only when a pawn is
    /// actually about to advance to another path cell. If a route is unsafe we stop movement here,
    /// then defer job cancellation to Pawn_JobTracker's next tick so job/path setup is never
    /// re-entered from inside the path follower.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareOutdoorWorkPatch
    {
        private const int LogCooldownTicks = 600;
        private const int MovingThreatRecheckTicks = 120;
        private const int CellsBetweenThreatChecks = 6;
        private const int HostileCacheTicks = 60;

        private sealed class PathCheckState
        {
            public PawnPath path;
            public int lastCheckTick = -999999;
            public int cellsSinceCheck = CellsBetweenThreatChecks;
            public bool blocked;
            public IntVec3 blockedDestination = IntVec3.Invalid;
            public IntVec3 blockedDangerCell = IntVec3.Invalid;
            public float blockedDangerRadius;
            public string blockedJobDef;
            public int blockedThingId = -1;
        }

        private sealed class GlobalDangerBlock
        {
            public int mapId;
            public int thingId = -1;
            public string jobDef;
            public IntVec3 destination = IntVec3.Invalid;
            public IntVec3 dangerCell = IntVec3.Invalid;
            public float dangerRadius;
        }

        private sealed class HostileCacheEntry
        {
            public int tick = -999999;
            public readonly List<Pawn> hostiles = new List<Pawn>();
        }

        private static readonly Dictionary<int, int> LastLogTickByPawn = new Dictionary<int, int>();
        private static readonly Dictionary<int, PathCheckState> CheckStateByPawn = new Dictionary<int, PathCheckState>();
        private static readonly Dictionary<long, HostileCacheEntry> HostileCache = new Dictionary<long, HostileCacheEntry>();
        private static readonly List<GlobalDangerBlock> GlobalBlocks = new List<GlobalDangerBlock>();
        private static readonly HashSet<long> BlockedThingKeys = new HashSet<long>();
        private static readonly List<IntVec3> RemainingPathCells = new List<IntVec3>(256);

        [HarmonyPrefix]
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            try
            {
                BetterRimAISettings settings = BetterRimAIMod.Settings;
                if (settings == null || !settings.threatAwareOutdoorWork)
                    return true;

                if (pawn == null || !pawn.Spawned || !IsProtectedPlayerPawn(pawn) || pawn.Drafted || pawn.CurJob == null)
                    return true;

                if (IsPlayerForcedJob(pawn.CurJob) || IsAttackOverride(pawn))
                {
                    ClearBlockedState(pawn.thingIDNumber);
                    return true;
                }

                int pawnId = pawn.thingIDNumber;
                if (!CheckStateByPawn.TryGetValue(pawnId, out PathCheckState state))
                {
                    state = new PathCheckState();
                    CheckStateByPawn[pawnId] = state;
                }

                Map map = pawn.Map;
                Area_Home home = map?.areaManager?.Home;
                if (map == null || home == null)
                    return true;

                // Home plus completely enclosed unpainted pockets are treated as protected base.
                if (DestinationIsInsideHome(__instance.Destination, map, home))
                {
                    ClearBlockedState(state);
                    return true;
                }

                // A mod can bypass ThinkNode/WorkGiver filtering and restart an already blocked job.
                // Stop movement immediately, but do not end the job from inside pathing.
                if (state.blocked && JobMatchesStateBlock(pawn.CurJob, state))
                {
                    CancelUnsafeCurrentJob(pawn, __instance);
                    return false;
                }

                PawnPath path = __instance.curPath;
                if (!__instance.Moving || path == null || !path.Found || path.Finished || path.NodesLeftCount <= 0)
                    return true;

                int tick = Find.TickManager?.TicksGame ?? 0;
                bool newPath = !ReferenceEquals(state.path, path);
                if (newPath)
                {
                    state.path = path;
                    state.cellsSinceCheck = CellsBetweenThreatChecks;
                }
                else
                {
                    state.cellsSinceCheck++;
                }

                bool cellRecheck = state.cellsSinceCheck >= CellsBetweenThreatChecks;
                bool timedRecheck = tick - state.lastCheckTick >= MovingThreatRecheckTicks;
                if (!newPath && !cellRecheck && !timedRecheck)
                    return true;

                state.lastCheckTick = tick;
                state.cellsSinceCheck = 0;

                RemainingPathCells.Clear();
                path.PeekNextCells(path.NodesLeftCount, RemainingPathCells, 0);
                if (RemainingPathCells.Count == 0)
                    return true;

                bool leavesProtectedBase = !ThreatAwareHomeSafety.IsSafeCell(map, home, pawn.Position);
                if (!leavesProtectedBase)
                {
                    for (int i = 0; i < RemainingPathCells.Count; i++)
                    {
                        IntVec3 cell = RemainingPathCells[i];
                        if (cell.InBounds(map) && !ThreatAwareHomeSafety.IsSafeCell(map, home, cell))
                        {
                            leavesProtectedBase = true;
                            break;
                        }
                    }
                }

                if (!leavesProtectedBase)
                {
                    ClearBlockedState(state);
                    return true;
                }

                List<Pawn> hostiles = GetRelevantHostilesCached(pawn, map, tick);
                if (hostiles.Count == 0)
                {
                    ClearBlockedState(state);
                    return true;
                }

                if (!TryFindUnsafeThreat(pawn, RemainingPathCells, home, hostiles, settings,
                        out Pawn threat, out string reason, out float closestDistance,
                        out IntVec3 dangerCell, out float dangerRadius))
                {
                    ClearBlockedState(state);
                    return true;
                }

                IntVec3 destination = __instance.Destination.IsValid
                    ? __instance.Destination.Cell
                    : RemainingPathCells[RemainingPathCells.Count - 1];

                Job unsafeJob = pawn.CurJob;
                state.blocked = true;
                state.blockedDestination = destination;
                state.blockedDangerCell = dangerCell;
                state.blockedDangerRadius = dangerRadius;
                state.blockedJobDef = unsafeJob?.def?.defName;
                state.blockedThingId = GetPrimaryThingId(unsafeJob);

                RememberGlobalBlock(map, unsafeJob, destination, dangerCell, dangerRadius);
                LogDecision(pawn, destination, threat, reason, closestDistance, settings);
                CancelUnsafeCurrentJob(pawn, __instance);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Threat-aware path check failed for " + pawn + ": " + ex);
                return true;
            }
        }

        public static bool IsProtectedPlayerPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Faction != Faction.OfPlayer || pawn.RaceProps == null)
                return false;

            // Tame animals have the player faction too, but this feature is intended for colonists,
            // player mechs/militors and modded drones, not ordinary colony animals.
            return !pawn.RaceProps.Animal;
        }

        public static bool CouldBeBlockedThing(Pawn pawn, Thing thing, bool forced)
        {
            if (forced || pawn == null || thing == null || pawn.Map == null || !IsProtectedPlayerPawn(pawn)
                || pawn.Drafted || IsAttackOverride(pawn))
                return false;

            return BlockedThingKeys.Contains(MakeThingKey(pawn.Map.uniqueID, thing.thingIDNumber));
        }

        public static bool ShouldSuppressWorkJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Map == null || !IsProtectedPlayerPawn(pawn)
                || pawn.Drafted || IsPlayerForcedJob(job) || IsAttackOverride(pawn))
                return false;

            int mapId = pawn.Map.uniqueID;
            int thingId = GetPrimaryThingId(job);
            string jobDef = job.def?.defName;
            TryGetJobDestination(job, out IntVec3 destination);

            if (thingId >= 0 && !BlockedThingKeys.Contains(MakeThingKey(mapId, thingId)))
                return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            for (int i = GlobalBlocks.Count - 1; i >= 0; i--)
            {
                GlobalDangerBlock block = GlobalBlocks[i];
                if (block.mapId != mapId || !BlockMatchesJob(block, thingId, jobDef, destination))
                    continue;

                List<Pawn> hostiles = GetRelevantHostilesCached(pawn, pawn.Map, tick);
                if (!ThreatStillNearCell(block.dangerCell, block.dangerRadius, hostiles))
                {
                    RemoveGlobalBlockAt(i);
                    continue;
                }
                return true;
            }
            return false;
        }

        private static bool IsPlayerForcedJob(Job job)
        {
            return job != null && job.playerForced;
        }

        private static void CancelUnsafeCurrentJob(Pawn pawn, Pawn_PathFollower pather)
        {
            if (pawn?.jobs == null) return;
            Job unsafeJob = pawn.CurJob;
            if (unsafeJob == null) return;

            // Critical: never EndCurrentJob/CheckForJobOverride while TryEnterNextPathCell is on
            // the stack. Stop movement now and let Pawn_JobTracker cancel the job on its next tick.
            pather.StopDead();
            ThreatAwarePendingCancellation.Schedule(pawn, unsafeJob);
        }

        private static bool IsAttackOverride(Pawn pawn)
        {
            return pawn.playerSettings != null
                   && pawn.playerSettings.UsesConfigurableHostilityResponse
                   && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack;
        }

        private static bool DestinationIsInsideHome(LocalTargetInfo destination, Map map, Area_Home home)
        {
            if (!destination.IsValid || map == null || home == null) return false;
            IntVec3 cell = destination.Cell;
            if (cell.IsValid && cell.InBounds(map) && ThreatAwareHomeSafety.IsSafeCell(map, home, cell)) return true;
            if (destination.HasThing && destination.Thing != null)
            {
                IntVec3 thingCell = destination.Thing.Position;
                return thingCell.IsValid && thingCell.InBounds(map)
                    && ThreatAwareHomeSafety.IsSafeCell(map, home, thingCell);
            }
            return false;
        }

        private static void RememberGlobalBlock(Map map, Job job, IntVec3 destination, IntVec3 dangerCell, float dangerRadius)
        {
            int thingId = GetPrimaryThingId(job);
            string jobDef = job?.def?.defName;
            for (int i = 0; i < GlobalBlocks.Count; i++)
            {
                GlobalDangerBlock existing = GlobalBlocks[i];
                if (existing.mapId == map.uniqueID && BlockMatchesJob(existing, thingId, jobDef, destination))
                {
                    existing.dangerCell = dangerCell;
                    existing.dangerRadius = dangerRadius;
                    if (thingId >= 0) BlockedThingKeys.Add(MakeThingKey(map.uniqueID, thingId));
                    return;
                }
            }

            GlobalBlocks.Add(new GlobalDangerBlock
            {
                mapId = map.uniqueID,
                thingId = thingId,
                jobDef = jobDef,
                destination = destination,
                dangerCell = dangerCell,
                dangerRadius = dangerRadius
            });

            if (thingId >= 0)
                BlockedThingKeys.Add(MakeThingKey(map.uniqueID, thingId));
        }

        private static void RemoveGlobalBlockAt(int index)
        {
            GlobalDangerBlock removed = GlobalBlocks[index];
            GlobalBlocks.RemoveAt(index);
            if (removed.thingId < 0) return;

            long key = MakeThingKey(removed.mapId, removed.thingId);
            for (int i = 0; i < GlobalBlocks.Count; i++)
            {
                if (GlobalBlocks[i].mapId == removed.mapId && GlobalBlocks[i].thingId == removed.thingId)
                    return;
            }
            BlockedThingKeys.Remove(key);
        }

        private static long MakeThingKey(int mapId, int thingId)
        {
            return ((long)mapId << 32) | (uint)thingId;
        }

        private static bool BlockMatchesJob(GlobalDangerBlock block, int thingId, string jobDef, IntVec3 destination)
        {
            if (block.thingId >= 0 && thingId >= 0) return block.thingId == thingId;
            return string.Equals(block.jobDef, jobDef, StringComparison.Ordinal)
                   && block.destination.IsValid && destination.IsValid && block.destination == destination;
        }

        private static bool JobMatchesStateBlock(Job job, PathCheckState state)
        {
            if (job == null || !state.blocked) return false;
            int thingId = GetPrimaryThingId(job);
            if (state.blockedThingId >= 0 && thingId >= 0) return state.blockedThingId == thingId;
            if (!string.Equals(job.def?.defName, state.blockedJobDef, StringComparison.Ordinal)) return false;
            return !TryGetJobDestination(job, out IntVec3 destination) || destination == state.blockedDestination;
        }

        private static bool ThreatStillNearCell(IntVec3 dangerCell, float dangerRadius, List<Pawn> hostiles)
        {
            if (!dangerCell.IsValid || dangerRadius <= 0f) return false;
            float radiusSquared = dangerRadius * dangerRadius;
            for (int i = 0; i < hostiles.Count; i++)
                if ((dangerCell - hostiles[i].Position).LengthHorizontalSquared <= radiusSquared) return true;
            return false;
        }

        private static int GetPrimaryThingId(Job job)
        {
            if (job == null) return -1;
            if (job.targetA.HasThing && job.targetA.Thing != null) return job.targetA.Thing.thingIDNumber;
            if (job.targetB.HasThing && job.targetB.Thing != null) return job.targetB.Thing.thingIDNumber;
            return -1;
        }

        private static void ClearBlockedState(int pawnId)
        {
            if (CheckStateByPawn.TryGetValue(pawnId, out PathCheckState state)) ClearBlockedState(state);
        }

        private static void ClearBlockedState(PathCheckState state)
        {
            state.blocked = false;
            state.blockedDestination = IntVec3.Invalid;
            state.blockedDangerCell = IntVec3.Invalid;
            state.blockedDangerRadius = 0f;
            state.blockedJobDef = null;
            state.blockedThingId = -1;
        }

        private static List<Pawn> GetRelevantHostilesCached(Pawn pawn, Map map, int tick)
        {
            long key = ((long)map.uniqueID << 32) | (uint)pawn.thingIDNumber;
            if (!HostileCache.TryGetValue(key, out HostileCacheEntry entry))
            {
                entry = new HostileCacheEntry();
                HostileCache[key] = entry;
            }

            if (tick - entry.tick < HostileCacheTicks)
                return entry.hostiles;

            entry.tick = tick;
            entry.hostiles.Clear();
            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn other = allPawns[i];
                if (other != pawn && !other.Dead && !other.Downed && other.Spawned && other.HostileTo(pawn))
                    entry.hostiles.Add(other);
            }
            return entry.hostiles;
        }

        private static bool TryFindUnsafeThreat(Pawn pawn, List<IntVec3> route, Area_Home home, List<Pawn> hostiles,
            BetterRimAISettings settings, out Pawn threat, out string reason, out float closestDistance,
            out IntVec3 dangerCell, out float dangerRadius)
        {
            threat = null;
            reason = null;
            closestDistance = float.MaxValue;
            dangerCell = IntVec3.Invalid;
            dangerRadius = 0f;

            Map map = pawn.Map;
            IntVec3 homeExitCell = IntVec3.Invalid;
            bool previousWasHome = pawn.Position.InBounds(map)
                && ThreatAwareHomeSafety.IsSafeCell(map, home, pawn.Position);

            for (int i = 0; i < route.Count; i++)
            {
                IntVec3 node = route[i];
                bool nodeIsHome = node.InBounds(map) && ThreatAwareHomeSafety.IsSafeCell(map, home, node);
                if (previousWasHome && !nodeIsHome)
                {
                    homeExitCell = node;
                    break;
                }
                previousWasHome = nodeIsHome;
            }

            if (homeExitCell.IsValid
                && TryFindThreatNearCell(homeExitCell, hostiles, settings.homeExitThreatRadius, out threat, out closestDistance))
            {
                reason = "hostile near protected-base exit";
                dangerCell = homeExitCell;
                dangerRadius = settings.homeExitThreatRadius;
                return true;
            }

            float routeRadiusSquared = settings.routeThreatRadius * settings.routeThreatRadius;
            for (int i = 0; i < route.Count; i += 3)
            {
                IntVec3 node = route[i];
                if (!node.InBounds(map) || ThreatAwareHomeSafety.IsSafeCell(map, home, node)) continue;

                for (int h = 0; h < hostiles.Count; h++)
                {
                    Pawn hostile = hostiles[h];
                    float distanceSquared = (node - hostile.Position).LengthHorizontalSquared;
                    if (distanceSquared > routeRadiusSquared) continue;

                    float distance = (float)Math.Sqrt(distanceSquared);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        threat = hostile;
                        dangerCell = node;
                        dangerRadius = settings.routeThreatRadius;
                    }
                }
            }

            if (threat != null)
            {
                reason = "hostile near actual remaining path";
                return true;
            }
            return false;
        }

        private static bool TryFindThreatNearCell(IntVec3 cell, List<Pawn> hostiles, float radius,
            out Pawn threat, out float closestDistance)
        {
            threat = null;
            closestDistance = float.MaxValue;
            float radiusSquared = radius * radius;
            for (int i = 0; i < hostiles.Count; i++)
            {
                float distanceSquared = (cell - hostiles[i].Position).LengthHorizontalSquared;
                if (distanceSquared <= radiusSquared && distanceSquared < closestDistance * closestDistance)
                {
                    closestDistance = (float)Math.Sqrt(distanceSquared);
                    threat = hostiles[i];
                }
            }
            return threat != null;
        }

        private static bool TryGetJobDestination(Job job, out IntVec3 destination)
        {
            if (job != null && job.targetA.IsValid)
            {
                destination = job.targetA.Cell;
                return destination.IsValid;
            }
            if (job != null && job.targetB.IsValid)
            {
                destination = job.targetB.Cell;
                return destination.IsValid;
            }
            destination = IntVec3.Invalid;
            return false;
        }

        private static void LogDecision(Pawn pawn, IntVec3 destination, Pawn threat, string reason,
            float closestDistance, BetterRimAISettings settings)
        {
            if (!settings.threatDebugLogging) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn.thingIDNumber;
            if (LastLogTickByPawn.TryGetValue(pawnId, out int lastTick) && tick - lastTick < LogCooldownTicks) return;
            LastLogTickByPawn[pawnId] = tick;
            Log.Message($"[BetterRimAI] {pawn.LabelShort}: stopped {pawn.CurJob?.def?.defName ?? "job"} toward {destination}; " +
                        $"{reason}, nearest={threat?.LabelShort ?? "unknown"} at {closestDistance:F0} cells.");
        }
    }
}
