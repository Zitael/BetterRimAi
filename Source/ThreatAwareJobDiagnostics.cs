using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Temporary diagnostics for unsafe jobs that appear to be immediately reissued by vanilla
    /// or another mod. Logs only jobs that BetterRimAI already knows are blocked, and only when
    /// threat debug logging is enabled.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class ThreatAwareStartJobDiagnostics
    {
        [HarmonyPrefix]
        public static void Prefix(
            Pawn ___pawn,
            Job newJob,
            ThinkNode jobGiver,
            bool fromQueue,
            ThinkTreeDef thinkTree)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatDebugLogging || ___pawn == null || newJob == null)
            {
                return;
            }

            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(___pawn, newJob))
            {
                return;
            }

            string workGiver = newJob.workGiverDef?.defName ?? "null";
            string sourceNode = jobGiver?.GetType().FullName ?? "null";
            string tree = thinkTree?.defName ?? "null";
            string current = ___pawn.CurJob?.def?.defName ?? "null";

            Log.Message(
                $"[BetterRimAI][diag] StartJob blocked target: pawn={___pawn.LabelShort}, " +
                $"newJob={newJob.def?.defName ?? "null"}, playerForced={newJob.playerForced}, " +
                $"fromQueue={fromQueue}, workGiver={workGiver}, sourceNode={sourceNode}, " +
                $"thinkTree={tree}, currentBeforeStart={current}.");
        }
    }
}
