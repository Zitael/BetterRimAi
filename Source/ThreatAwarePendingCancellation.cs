using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Cancelling a job from inside Pawn_PathFollower.TryEnterNextPathCell can re-enter job/path
    /// setup while the old movement transition is still on the stack. That is what caused pawns to
    /// oscillate between two cells. The movement guard now only stops pathing and schedules the
    /// cancellation; Pawn_JobTracker consumes it at the beginning of its next tick.
    /// </summary>
    [HarmonyPatch]
    public static class ThreatAwarePendingCancellation
    {
        private static readonly Dictionary<long, Job> PendingByPawn = new Dictionary<long, Job>();

        public static void Schedule(Pawn pawn, Job unsafeJob)
        {
            if (pawn?.Map == null || unsafeJob == null)
                return;
            PendingByPawn[PawnKey(pawn)] = unsafeJob;
        }

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
            if (pawn?.Map == null || pawn.jobs == null)
                return;

            long key = PawnKey(pawn);
            if (!PendingByPawn.TryGetValue(key, out Job unsafeJob))
                return;

            PendingByPawn.Remove(key);

            // A direct order or another mod may have replaced the job before the next tracker tick.
            // In that case the stale cancellation must not touch the new job.
            if (!ReferenceEquals(pawn.CurJob, unsafeJob))
                return;

            pawn.jobs.jobQueue.RemoveAll(pawn, queuedJob =>
                ReferenceEquals(queuedJob, unsafeJob)
                || ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, queuedJob));

            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
        }

        private static long PawnKey(Pawn pawn)
        {
            return ((long)pawn.Map.uniqueID << 32) | (uint)pawn.thingIDNumber;
        }
    }
}
