using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// RimWorld 1.6 pathfinding is asynchronous. Instead of trying to generate a path ourselves,
    /// inspect Pawn_PathFollower.curPath after vanilla has calculated it. This makes the safety
    /// check apply to both automatic and player-forced civilian jobs while still allowing drafted
    /// pawns and pawns whose hostility response is Attack to follow player intent.
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
                    return true;
                }

                PawnPath path = __instance.curPath;
                if (!__instance.Moving || path == null || !path.Found || path.Finished || path.NodesLeftCount <= 0)
                {
                    return true;
                }

                int tick = Find.TickManager?.TicksGame ?? 0;
                int pawnId = pawn.thingIDNumber;
                if (!CheckStateByPawn.TryGetValue(pawnId, out PathCheckState state))
                {
                    state = new PathCheckState();
                    CheckStateByPawn[pawnId] = state;
                }

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

                Map map = pawn.Map;
                Area_Home home = map?.areaManager?.Home;
                if (map == null || home == null)
                {
                    return true;
                }

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
                    return true;
                }

                List<Pawn> hostiles = GetRelevantHostiles(pawn, map);
                if (hostiles.Count == 0)
                {
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
                        out float closestDistance))
                {
                    return true;
                }

                IntVec3 destination = __instance.Destination.IsValid
                    ? __instance.Destination.Cell
                    : RemainingPathCells[RemainingPathCells.Count - 1];

                LogDecision(pawn, destination, threat, reason, closestDistance, settings);

                // Safety applies to civilian movement even for a direct right-click order.
                // Drafting the pawn or selecting Attack response is the explicit override.
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Threat-aware path check failed for " + pawn + ": " + ex);
                return true;
            }
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
            out float closestDistance)
        {
            threat = null;
            reason = null;
            closestDistance = float.MaxValue;

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
