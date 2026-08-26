using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Universal fallback for autonomous jobs that do not come from WorkGiver_Scanner
    /// (needs, hygiene mods, recreation mods, custom JobGivers, etc.).
    ///
    /// Returning NoJob lets the parent priority node continue to another activity instead of
    /// immediately restarting an unsafe outdoor job.
    /// </summary>
    [HarmonyPatch(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage))]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareThinkNodePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref ThinkResult __result)
        {
            if (!__result.IsValid || pawn == null)
                return;

            Job job = __result.Job;
            if (job == null || job.playerForced)
                return;

            bool retryCooldown = ThreatAwareOutdoorRetryCooldown.ShouldSuppressOutdoorRetry(pawn, job);
            bool blockedTarget = !retryCooldown && ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job);
            if (!retryCooldown && !blockedTarget)
                return;

            Thing thing = null;
            if (job.targetA.HasThing) thing = job.targetA.Thing;
            else if (job.targetB.HasThing) thing = job.targetB.Thing;

            ThreatAwareBlockDiagnostics.Once(
                retryCooldown ? "thinknode-outdoor-cooldown" : "thinknode-blocked-target",
                pawn,
                thing,
                job,
                true,
                retryCooldown
                    ? "temporary backoff after unsafe outdoor cancellation"
                    : "known unsafe target");

            __result = ThinkResult.NoJob;
        }
    }
}
