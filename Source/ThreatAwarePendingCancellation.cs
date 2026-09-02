using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Cancelling from Pawn_PathFollower.TryEnterNextPathCell is unsafe because it can re-enter
    /// job/path setup while the old movement transition is still on the stack. The movement guard
    /// therefore only stops pathing and schedules a cancellation. Pawn_JobTracker consumes it at
    /// the beginning of its next tick, where replacing the current job is safe.
    ///
    /// Important: do not rely only on Job reference equality here. Some job givers/mods can replace
    /// the stopped Job with a fresh equivalent Job before this prefix runs. In that case the old
    /// implementation considered the cancellation stale and left the pawn stuck on the same unsafe
    /// activity. We now also recognise any currently-blocked job and any autonomous outdoor retry
    /// covered by the per-pawn cooldown.
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

            Thing thing = unsafeJob.targetA.HasThing ? unsafeJob.targetA.Thing
                : unsafeJob.targetB.HasThing ? unsafeJob.targetB.Thing
                : null;
            ThreatAwareBlockDiagnostics.Once(
                "cancel-scheduled",
                pawn,
                thing,
                unsafeJob,
                true,
                "deferred from path follower to Pawn_JobTracker");
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
            if (!PendingByPawn.TryGetValue(key, out Job originallyUnsafeJob))
                return;

            PendingByPawn.Remove(key);

            Job current = pawn.CurJob;
            if (current == null)
                return;

            // Exact old job, a fresh equivalent job that still matches the global danger block,
            // or any autonomous outdoor retry during the cooldown should be terminated here.
            bool exactJob = ReferenceEquals(current, originallyUnsafeJob);
            bool stillBlocked = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, current);
            bool outdoorRetry = ThreatAwareOutdoorRetryCooldown.ShouldSuppressOutdoorRetry(pawn, current);

            if (!exactJob && !stillBlocked && !outdoorRetry)
                return; // Another mod/player legitimately replaced it with a safe job.

            pawn.jobs.jobQueue.RemoveAll(pawn, queuedJob =>
                ReferenceEquals(queuedJob, originallyUnsafeJob)
                || ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, queuedJob)
                || ThreatAwareOutdoorRetryCooldown.ShouldSuppressOutdoorRetry(pawn, queuedJob));

            Thing thing = current.targetA.HasThing ? current.targetA.Thing
                : current.targetB.HasThing ? current.targetB.Thing
                : null;
            ThreatAwareBlockDiagnostics.Once(
                "cancel-consumed",
                pawn,
                thing,
                current,
                true,
                exactJob ? "same Job instance" : stillBlocked ? "reissued blocked Job" : "outdoor retry during cooldown");

            // We are now in Pawn_JobTracker, not inside path following, so normal replacement-job
            // selection is safe. The cooldown/ThinkNode filter is already active and prevents the
            // next autonomous job from immediately walking back outside.
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable, startNewJob: true);
        }

        private static long PawnKey(Pawn pawn)
        {
            return ((long)pawn.Map.uniqueID << 32) | (uint)pawn.thingIDNumber;
        }
    }
}
