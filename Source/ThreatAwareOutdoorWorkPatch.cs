using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// RimWorld 1.6 pathfinding is asynchronous. Inspect the actual PawnPath once vanilla has
    /// calculated it, then remember the unsafe route point so repeated/reissued jobs cannot creep
    /// forward one cell at a time while a new PathRequest is being calculated.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.PatherTick))]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareOutdoorWorkPatch
    {
        private const int LogCooldownTicks = 600;
        private const int MovingThreatRecheckTicks = 30;

        private sealed class PathCheckState
        {
            public PawnPath path;
            public IntVec3 nextCell = IntVec3.Invalid;
            public int lastCheckTick = -999999;

            // When a path was found unsafe, remember enough information to reject a freshly
            // reissued copy of the same job before its asynchronous path has even completed.
            public bool blocked;
            public IntVec3 blockedDestination = IntVec3.Invalid;
            public IntVec3 blockedDangerCell = IntVec3.Invalid;
            public float blockedDangerRadius;
            public string blockedJobDef;
        }

        private static readonly Dictionary<int, int> LastLogTickByPawn = new Dictionary<int, int>();
        private static readonly Dictionary<int, PathCheckState> CheckStateByPawn = new Dictionary<int, PathCheckState>();
        private static readonly List<IntVec3> RemainingPathCells = new List<IntVec3>(256);

        [HarmonyPrefix]
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;

            try
            {
                BetterRimAISettings settings = BetterRimAIMod.Settings;
                if (settings == null || !settings.threatAwareOutdoorWork)
                {
                    return true;
                }

                if (pawn == null || !pawn.Spawned || !pawn.IsColonist || pawn.Drafted || pawn.CurJob == null)
                {
                    return true;
                }

                if (pawn.playerSettings != null
                    && pawn.playerSettings.UsesConfigurableHostilityResponse
                    && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack)
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
                {
                    return true;
                }

                // Critical 1.6 case: a cancelled hauling job may immediately be reissued. During
                // the few ticks while the replacement PathRequest is calculating, curPath is null.
                // Without this guard vanilla gets another PatherTick and the pawn can creep forward
                // one cell per restart. Reject the same unsafe job before a new path exists.
                if (state.blocked && JobMatchesBlock(pawn.CurJob, state))
                {
                    List<Pawn> currentHostiles = GetRelevantHostiles(pawn, map);
                    if (ThreatStillNearBlockedCell(state, currentHostiles))
                    {
                        __instance.StopDead();
                        pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return false;
                    }

                    ClearBlockedState(state);
                }

                PawnPath path = __instance.curPath;
                if (!__instance.Moving || path == null || !path.Found || path.Finished || path.NodesLeftCount <= 0)
                {
                    return true;
                }

                int tick = Find.TickManager?.TicksGame ?? 0;
                bool newPath = !ReferenceEquals(state.path, path);
                bool newCell = state.nextCell != __instance.nextCell;
                bool timedRecheck = tick - state.lastCheckTick >= MovingThreatRecheckTicks;
                if (!newPath && !newCell && !timedRecheck)
                {
                    return true;
                }

                state.path = path;
                state.nextCell = __instance.nextCell;
                state.lastCheckTick = tick;

                RemainingPathCells.Clear();
                path.PeekNextCells(path.NodesLeftCount, RemainingPathCells, 0);
                if (RemainingPathCells.Count == 0)
                {
                    return true;
                }

                bool leavesHome = !home[pawn.Position];
                if (!leavesHome)
                {
                    for (int i = 0; i < RemainingPathCells.Count; i++)
                    {
                        IntVec3 cell = RemainingPathCells[i];
                        if (cell.InBounds(map) && !home[cell])
                        {
                            leavesHome = true;
                            break;
                        }
                    }
                }

                if (!leavesHome)
                {
                    ClearBlockedState(state);
                    return true;
                }

                List<Pawn> hostiles = GetRelevantHostiles(pawn, map);
                if (hostiles.Count == 0)
                {
                    ClearBlockedState(state);
                    return true;
                }

                if (!TryFindUnsafeThreat(
                        pawn,
                        RemainingPathCells,
                        home,
                        hostiles,
                        settings,
                        out Pawn threat,
                        out string reason,
                        out float closestDistance,
                        out IntVec3 dangerCell,
                        out float dangerRadius))
                {
                    ClearBlockedState(state);
                    return true;
                }

                IntVec3 destination = __instance.Destination.IsValid
                    ? __instance.Destination.Cell
                    : RemainingPathCells[RemainingPathCells.Count - 1];

                state.blocked = true;
                state.blockedDestination = destination;
                state.blockedDangerCell = dangerCell;
                state.blockedDangerRadius = dangerRadius;
                state.blockedJobDef = pawn.CurJob?.def?.defName;

                LogDecision(pawn, destination, threat, reason, closestDistance, settings);

                // Stop movement first, then terminate the civilian job. If a hauling mod or the
                // vanilla thinker immediately creates the same job again, the block above rejects
                // it before the replacement async path can move the pawn at all.
                __instance.StopDead();
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Threat-aware path check failed for " + pawn + ": " + ex);
                return true;
            }
        }

        private static bool JobMatchesBlock(Job job, PathCheckState state)
        {
            if (job == null || !state.blocked)
            {
                return false;
            }

            string jobDef = job.def?.defName;
            if (!string.Equals(jobDef, state.blockedJobDef, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryGetJobDestination(job, out IntVec3 destination))
            {
                // Some multi-stage hauling jobs change their immediate target while keeping the
                // same job def. Treat them as the blocked job while the danger remains.
                return true;
            }

            return destination == state.blockedDestination;
        }

        private static bool ThreatStillNearBlockedCell(PathCheckState state, List<Pawn> hostiles)
        {
            if (!state.blockedDangerCell.IsValid || state.blockedDangerRadius <= 0f)
            {
                return false;
            }

            float radiusSquared = state.blockedDangerRadius * state.blockedDangerRadius;
            for (int i = 0; i < hostiles.Count; i++)
            {
                Pawn hostile = hostiles[i];
                if ((state.blockedDangerCell - hostile.Position).LengthHorizontalSquared <= radiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearBlockedState(int pawnId)
        {
            if (CheckStateByPawn.TryGetValue(pawnId, out PathCheckState state))
            {
                ClearBlockedState(state);
            }
        }

        private static void ClearBlockedState(PathCheckState state)
        {
            state.blocked = false;
            state.blockedDestination = IntVec3.Invalid;
            state.blockedDangerCell = IntVec3.Invalid;
            state.blockedDangerRadius = 0f;
            state.blockedJobDef = null;
        }

        private static List<Pawn> GetRelevantHostiles(Pawn pawn, Map map)
        {
            List<Pawn> result = new List<Pawn>();
            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn other = allPawns[i];
                if (other == pawn || other.Dead || other.Downed || !other.Spawned)
                {
                    continue;
                }

                if (other.HostileTo(pawn))
                {
                    result.Add(other);
                }
            }

            return result;
        }

        private static bool TryFindUnsafeThreat(
            Pawn pawn,
            List<IntVec3> route,
            Area_Home home,
            List<Pawn> hostiles,
            BetterRimAISettings settings,
            out Pawn threat,
            out string reason,
            out float closestDistance,
            out IntVec3 dangerCell,
            out float dangerRadius)
        {
            threat = null;
            reason = null;
            closestDistance = float.MaxValue;
            dangerCell = IntVec3.Invalid;
            dangerRadius = 0f;

            Map map = pawn.Map;
            IntVec3 homeExitCell = IntVec3.Invalid;
            bool previousWasHome = pawn.Position.InBounds(map) && home[pawn.Position];

            for (int i = 0; i < route.Count; i++)
            {
                IntVec3 node = route[i];
                bool nodeIsHome = node.InBounds(map) && home[node];

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
                reason = "hostile near Home-area exit";
                dangerCell = homeExitCell;
                dangerRadius = settings.homeExitThreatRadius;
                return true;
            }

            float routeRadiusSquared = settings.routeThreatRadius * settings.routeThreatRadius;

            // Only inspect the part of the remaining route outside Home. Hostiles elsewhere on
            // the map are irrelevant, including roaming shamblers on the far side of the map.
            for (int i = 0; i < route.Count; i += 2)
            {
                IntVec3 node = route[i];
                if (!node.InBounds(map) || home[node])
                {
                    continue;
                }

                for (int h = 0; h < hostiles.Count; h++)
                {
                    Pawn hostile = hostiles[h];
                    float distanceSquared = (node - hostile.Position).LengthHorizontalSquared;
                    if (distanceSquared > routeRadiusSquared)
                    {
                        continue;
                    }

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

        private static bool TryFindThreatNearCell(
            IntVec3 cell,
            List<Pawn> hostiles,
            float radius,
            out Pawn threat,
            out float closestDistance)
        {
            threat = null;
            closestDistance = float.MaxValue;
            float radiusSquared = radius * radius;

            for (int i = 0; i < hostiles.Count; i++)
            {
                Pawn hostile = hostiles[i];
                float distanceSquared = (cell - hostile.Position).LengthHorizontalSquared;
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                float distance = (float)Math.Sqrt(distanceSquared);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    threat = hostile;
                }
            }

            return threat != null;
        }

        private static bool TryGetJobDestination(Job job, out IntVec3 destination)
        {
            if (job.targetA.IsValid)
            {
                destination = job.targetA.Cell;
                return destination.IsValid;
            }

            if (job.targetB.IsValid)
            {
                destination = job.targetB.Cell;
                return destination.IsValid;
            }

            destination = IntVec3.Invalid;
            return false;
        }

        private static void LogDecision(
            Pawn pawn,
            IntVec3 destination,
            Pawn threat,
            string reason,
            float closestDistance,
            BetterRimAISettings settings)
        {
            if (!settings.threatDebugLogging)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn.thingIDNumber;
            if (LastLogTickByPawn.TryGetValue(pawnId, out int lastTick) && tick - lastTick < LogCooldownTicks)
            {
                return;
            }

            LastLogTickByPawn[pawnId] = tick;
            string threatLabel = threat == null ? "unknown hostile" : threat.LabelShort;
            string jobLabel = pawn.CurJob?.def?.defName ?? "unknown job";
            Log.Message($"[BetterRimAI] {pawn.LabelShort}: stopped {jobLabel} toward {destination}; {reason}, nearest={threatLabel} at {closestDistance:F0} cells.");
        }
    }
}
