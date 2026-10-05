using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    // The sole deferred state required by the last-resort path guard. Never re-enter job
    // selection from TryEnterNextPathCell; consume on the next job-tracker tick instead.
    [HarmonyPatch]
    public static class ThreatAwarePendingCancellation
    {
        private static readonly Dictionary<Pawn, Job> pending = new Dictionary<Pawn, Job>();
        internal static void Reset() => pending.Clear();
        public static void Schedule(Pawn pawn, Job job) { if (pawn != null && job != null) pending[pawn] = job; }

        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            MethodInfo tick = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTick));
            if (tick != null) yield return tick;
            MethodInfo interval = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval));
            if (interval != null) yield return interval;
        }

        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            if (pawn == null || !pending.TryGetValue(pawn, out Job oldJob)) return;
            pending.Remove(pawn);
            Job current = pawn.CurJob;
            if (current == null) { ThreatAwareRuntimeTrace.CancelSkipped(pawn, null, "no current job"); return; }
            if (!ThreatAwareOutdoorPolicy.Applies(pawn, current.playerForced))
            { ThreatAwareRuntimeTrace.CancelSkipped(pawn, current, "override or disabled"); return; }
            Area_Home home = pawn.Map?.areaManager?.Home;
            if (home == null || !ThreatAwareHomeSafety.IsSafeCell(pawn.Map, home, pawn.Position))
            { ThreatAwareRuntimeTrace.CancelSkipped(pawn, current, "pawn outside protected area"); return; }
            if (!ReferenceEquals(current, oldJob) && !ThreatAwareOutdoorPolicy.Reject(pawn, current))
            { ThreatAwareRuntimeTrace.CancelSkipped(pawn, current, "safe replacement job"); return; }
            ThreatAwareRuntimeTrace.Cancelled(pawn, current, ReferenceEquals(current, oldJob));
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
        }
    }
}
