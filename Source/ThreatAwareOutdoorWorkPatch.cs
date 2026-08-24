using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Stops non-combat colonists from starting automatic work outside the Home area when
    /// the actual vanilla route passes close to a hostile pawn. Hostiles elsewhere on the map
    /// are intentionally ignored.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    [HarmonyPriority(Priority.Low)]
    public static class ThreatAwareOutdoorWorkPatch
    {
        private const int LogCooldownTicks = 600;
        private static readonly Dictionary<int, int> LastLogTickByPawn = new Dictionary<int, int>();

        [HarmonyPostfix]
        public static void Postfix(JobGiver_Work __instance, Pawn pawn, ref ThinkResult __result)
        {
            try
            {
                BetterRimAISettings settings = BetterRimAIMod.Settings;
                if (settings == null || !settings.threatAwareOutdoorWork)
                {
                    return;
                }

                if (pawn == null || !pawn.Spawned || !pawn.IsColonist || pawn.Drafted || __instance.emergency || !__result.IsValid)
                {
                    return;
                }

                Job job = __result.Job;
                if (job == null || job.playerForced)
                {
                    return;
                }

                if (pawn.playerSettings != null
                    && pawn.playerSettings.UsesConfigurableHostilityResponse
                    && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack)
                {
                    return;
                }

                if (!TryGetDestination(job, out IntVec3 destination))
                {
                    return;
                }

                Map map = pawn.Map;
                if (map == null || !destination.InBounds(map))
                {
                    return;
                }

                Area_Home home = map.areaManager?.Home;
                if (home == null || home[destination])
                {
                    return;
                }

                List<Pawn> hostiles = GetRelevantHostiles(pawn, map);
                if (hostiles.Count == 0)
                {
                    return;
                }

                // RimWorld 1.6 no longer exposes the old PathFinder.FindPath API to mods.
                // Use the pawn's pather, which delegates to vanilla pathfinding and gives us
                // the same route the pawn would actually follow.
                PawnPath path = pawn.pather?.TryFindPath(destination, PathEndMode.Touch);
                if (path == null || path == PawnPath.NotFound)
                {
                    return;
                }

                try
                {
                    if (TryFindUnsafeThreat(pawn, path, home, hostiles, settings, out Pawn threat, out string reason, out float closestDistance))
                    {
                        __result = ThinkResult.NoJob;
                        LogDecision(pawn, destination, threat, reason, closestDistance, settings);
                    }
                }
                finally
                {
                    path.ReleaseToPool();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Threat-aware outdoor work check failed for " + pawn + ": " + ex);
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
            PawnPath path,
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

            List<IntVec3> nodes = path.NodesReversed;
            if (nodes == null || nodes.Count == 0)
            {
                return false;
            }

            IntVec3 homeExitCell = IntVec3.Invalid;
            bool previousWasHome = pawn.Position.InBounds(pawn.Map) && home[pawn.Position];

            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                IntVec3 node = nodes[i];
                bool nodeIsHome = node.InBounds(pawn.Map) && home[node];

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

            for (int i = nodes.Count - 1; i >= 0; i -= 2)
            {
                IntVec3 node = nodes[i];
                for (int h = 0; h < hostiles.Count; h++)
                {
                    Pawn hostile = hostiles[h];
                    float distanceSquared = (node - hostile.Position).LengthHorizontalSquared;
                    if (distanceSquared <= routeRadiusSquared)
                    {
                        float distance = (float)Math.Sqrt(distanceSquared);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            threat = hostile;
                        }
                    }
                }
            }

            IntVec3 destination = nodes[0];
            for (int h = 0; h < hostiles.Count; h++)
            {
                Pawn hostile = hostiles[h];
                float distanceSquared = (destination - hostile.Position).LengthHorizontalSquared;
                if (distanceSquared <= routeRadiusSquared)
                {
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
                reason = "hostile near calculated route";
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

        private static bool TryGetDestination(Job job, out IntVec3 destination)
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
            Log.Message($"[BetterRimAI] {pawn.LabelShort}: blocked outdoor work at {destination}; {reason}, nearest={threatLabel} at {closestDistance:F0} cells.");
        }
    }
}
