using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Universal fallback for autonomous jobs that do not come from WorkGiver_Scanner
    /// (needs, hygiene mods, recreation mods, custom JobGivers, etc.).
    ///
    /// ThinkNode_JobGiver.TryIssueJobPackage is the common adapter from TryGiveJob() to a
    /// ThinkResult. Returning NoJob here is important: the parent priority node can then
    /// continue to its next child instead of letting the pawn start/cancel the same unsafe
    /// job forever.
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

            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job))
                return;

            if (BetterRimAIMod.Settings?.threatDebugLogging == true)
            {
                Log.Message($"[BetterRimAI] {pawn.LabelShort}: suppressed autonomous {job.def?.defName ?? "job"} " +
                            "at ThinkNode_JobGiver so AI can choose another job.");
            }

            __result = ThinkResult.NoJob;
        }
    }
}
