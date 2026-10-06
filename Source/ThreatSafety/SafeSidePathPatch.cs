using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Safe-side Touch work: while a threat is active, a protected pawn doing autonomous Touch
    /// work on a target whose exterior side is unsafe walks to a protected cell from which
    /// vanilla accepts the Touch (an outer wall repaired from inside), instead of possibly
    /// stepping outside. The cell is verified with vanilla's own touch and reachability checks,
    /// so the job driver's "can touch" check passes on arrival.
    /// With no active threat, or with a safe exterior side, the path is left exactly as vanilla
    /// requested it.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class SafeSidePathPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, ref LocalTargetInfo __0, ref PathEndMode __1)
        {
            if (__1 != PathEndMode.Touch && __1 != PathEndMode.ClosestTouch) return;
            Pawn pawn = ___pawn;
            Job job = pawn?.CurJob;
            if (job == null || job.playerForced || job.workGiverDef == null || !ThreatSafetyPolicy.Eligible(pawn)) return;
            MapThreatState state = MapThreatState.For(pawn.Map);
            if (!state.Active) return;
            int component = state.ComponentAt(pawn.Position);
            if (component == 0) return;

            LocalTargetInfo target = __0;
            IntVec3 cell;
            try
            {
                if (ThreatSafetyPolicy.Classify(state, pawn, component, target, __1, out cell) != Verdict.SafeSide) return;
                if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;
                if (!ReachabilityImmediate.CanReachImmediate(cell, target, pawn.Map, PathEndMode.Touch, pawn)) return;
            }
            catch (Exception ex)
            {
                ThreatSafetyLog.Failed("safe-side path check", ex);
                return;
            }

            if (!state.Active) ThreatSafetyLog.Invariant("safe-side path rewrite", pawn, job);
            __0 = cell;
            __1 = PathEndMode.OnCell;
            ThreatSafetyLog.SafeSide(pawn, job, target, cell);
        }
    }
}
