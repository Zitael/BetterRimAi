using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    // The sole SAFE -> OUTSIDE policy. WorkGivers, ThinkNodes, PUAH and the last-resort
    // path guard all call this class; no JobDef or target is globally blacklisted.
    internal static class ThreatAwareOutdoorPolicy
    {
        internal static bool Applies(Pawn pawn, bool forced)
        {
            if (forced || pawn?.Map == null || pawn.Drafted
                || BetterRimAIMod.Settings?.threatAwareOutdoorWork != true
                || !ThreatAwareOutdoorWorkPatch.IsProtectedPlayerPawn(pawn)) return false;
            return pawn.playerSettings == null || !pawn.playerSettings.UsesConfigurableHostilityResponse
                   || pawn.playerSettings.hostilityResponse != HostilityResponseMode.Attack;
        }

        internal static bool Reject(Pawn pawn, LocalTargetInfo target, bool forced, bool safeTouch = false)
            => Reject(pawn, target, forced, safeTouch, out _);

        internal static bool Reject(Pawn pawn, LocalTargetInfo target, bool forced, bool safeTouch,
            out string reason)
        {
            reason = "override or feature disabled";
            if (!Applies(pawn, forced)) return false;
            Map map = pawn.Map;
            Area_Home home = map.areaManager?.Home;
            if (home == null || !target.IsValid || !target.Cell.InBounds(map))
            { reason = "no protected target"; return false; }
            if (!ThreatAwareHomeSafety.IsSafeCell(map, home, pawn.Position))
            { reason = "pawn already outside"; return false; }
            if (ThreatAwareHomeSafety.IsSafeCell(map, home, target.Cell))
            { reason = "target protected"; return false; }
            // Actual-route evidence wins over a theoretical protected Touch cell. Vanilla
            // already chose an exposed approach to this job on a previous attempt.
            if (ThreatAwareOutdoorSafetyMap.HasObservedRoute(pawn, target, out Thing observedThreat))
            {
                reason = "observed unsafe route";
                ThreatAwareDecision.Remember(pawn, target, observedThreat, reason, null);
                return true;
            }
            if (safeTouch && ThreatAwareSafeWorkCell.TryFind(pawn, target, false, out _))
            { reason = "protected Touch cell"; return false; }
            if (!ThreatAwareOutdoorSafetyMap.Unsafe(pawn, target, out Thing threat, out reason)) return false;
            ThreatAwareDecision.Remember(pawn, target, threat, reason, null);
            return true;
        }

        internal static bool Reject(Pawn pawn, Job job, bool safeTouch = false)
            => Reject(pawn, job, safeTouch, out _);

        internal static bool Reject(Pawn pawn, Job job, bool safeTouch, out string reason)
        {
            reason = "override or no job";
            if (job == null || !Applies(pawn, job.playerForced)) return false;
            if (!safeTouch && job.workGiverDef?.Worker is WorkGiver_Scanner scanner)
                safeTouch = scanner.PathEndMode == PathEndMode.Touch;
            bool rejected = Reject(pawn, job.targetA, false, safeTouch, out reason);
            if (!rejected && job.targetB.IsValid) rejected = Reject(pawn, job.targetB, false, false, out reason);
            if (!rejected && job.targetC.IsValid) rejected = Reject(pawn, job.targetC, false, false, out reason);
            if (!rejected && job.targetQueueA != null && job.targetQueueA.Count != 0)
                rejected = RejectQueue(pawn, job.targetQueueA, out reason);
            if (!rejected && job.targetQueueB != null && job.targetQueueB.Count != 0)
                rejected = RejectQueue(pawn, job.targetQueueB, out reason);
            if (rejected) ThreatAwareDecision.SetJob(pawn, job);
            return rejected;
        }

        private static bool RejectQueue(Pawn pawn, List<LocalTargetInfo> queue, out string reason)
        {
            reason = "no unsafe queued target";
            if (queue == null) return false;
            for (int i = 0; i < queue.Count; i++)
                if (Reject(pawn, queue[i], false, false, out reason)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareOutdoorWorkPatch
    {
        private static readonly List<IntVec3> PathCells = new List<IntVec3>(256);
        internal static void Reset()
        {
            ThreatAwareOutdoorSafetyMap.Reset();
            ThreatAwareDecision.Reset();
        }

        public static bool IsProtectedPlayerPawn(Pawn pawn)
            => pawn != null && pawn.Faction == Faction.OfPlayer && pawn.RaceProps != null && !pawn.RaceProps.Animal;

        public static bool CouldBeBlockedThing(Pawn pawn, Thing thing, bool forced, bool safeTouch = false)
            => thing != null && ThreatAwareOutdoorPolicy.Reject(pawn, thing, forced, safeTouch);

        internal static bool CouldBeBlockedThing(Pawn pawn, Thing thing, bool forced, bool safeTouch,
            out string reason)
        {
            reason = "no Thing target";
            return thing != null && ThreatAwareOutdoorPolicy.Reject(pawn, thing, forced, safeTouch, out reason);
        }

        internal static bool ShouldSuppressCandidate(Pawn pawn, LocalTargetInfo target, bool forced, bool safeTouch = false)
            => ThreatAwareOutdoorPolicy.Reject(pawn, target, forced, safeTouch);

        internal static bool ShouldSuppressCandidate(Pawn pawn, LocalTargetInfo target, bool forced,
            bool safeTouch, out string reason)
            => ThreatAwareOutdoorPolicy.Reject(pawn, target, forced, safeTouch, out reason);

        public static bool ShouldSuppressWorkJob(Pawn pawn, Job job, bool safeTouch = false)
            => ThreatAwareOutdoorPolicy.Reject(pawn, job, safeTouch);

        // Only a genuinely unsafe crossing from a protected cell can be stopped. Once a pawn
        // is outside, its return and all other movement remain vanilla's responsibility.
        [HarmonyPrefix]
        public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            try
            {
                if (!ThreatAwareOutdoorPolicy.Applies(pawn, pawn?.CurJob?.playerForced == true)) return true;
                Map map = pawn.Map;
                Area_Home home = map.areaManager?.Home;
                if (home == null || !ThreatAwareHomeSafety.IsSafeCell(map, home, pawn.Position)) return true;
                PawnPath path = __instance.curPath;
                if (!__instance.Moving || path == null || !path.Found || path.Finished || path.NodesLeftCount == 0) return true;
                // Ordinary indoor steps do no threat work. Inspect the remaining path only
                // at the actual protected-area crossing, with a reused scratch list.
                PathCells.Clear();
                path.PeekNextCells(1, PathCells, 0);
                if (PathCells.Count == 0 || ThreatAwareHomeSafety.IsSafeCell(map, home, PathCells[0])) return true;
                ThreatAwareOutdoorSafetyMap.RefreshForDeparture(pawn);
                PathCells.Clear();
                path.PeekNextCells(path.NodesLeftCount, PathCells, 0);
                for (int i = 0; i < PathCells.Count; i++)
                {
                    IntVec3 cell = PathCells[i];
                    if (!cell.InBounds(map) || ThreatAwareHomeSafety.IsSafeCell(map, home, cell)) continue;
                    if (!ThreatAwareOutdoorSafetyMap.ThreatensCell(pawn, cell, out Thing threat)) continue;
                    Job job = pawn.CurJob;
                    ThreatAwareOutdoorSafetyMap.ObserveUnsafeJob(pawn, job, __instance.Destination, cell);
                    ThreatAwareDecision.Remember(pawn, cell, threat, "active path exposed", job);
                    ThreatAwareRuntimeTrace.Guard(pawn, job, __instance.Destination, cell, threat,
                        "path destination and exterior Job targets");
                    ThreatAwareBlockDiagnostics.Once("active-path-cancelled-safety-net", pawn, threat, job, true);
                    __instance.StopDead();
                    ThreatAwarePendingCancellation.Schedule(pawn, job);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Threat-aware path check failed for " + pawn + ": " + ex);
                return true;
            }
        }
    }
}
