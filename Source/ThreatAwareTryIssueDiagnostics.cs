using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Temporary narrow diagnostic for jobs returned directly by JobGiver_Work.TryIssueJobPackage.
    /// This catches paths that do not pass through GiverTryGiveJobPrioritized.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "TryIssueJobPackage")]
    public static class ThreatAwareTryIssueDiagnostics
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, JobIssueParams jobParams, ThinkResult __result)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatDebugLogging || pawn == null)
                return;

            Job job = __result.Job;
            if (job == null || !string.Equals(job.def?.defName, "HaulToInventory", StringComparison.Ordinal))
                return;

            ThinkNode source = __result.SourceNode;
            Type sourceType = source?.GetType();

            Log.Message(
                $"[BetterRimAI][try-issue] pawn={pawn.LabelShort}, " +
                $"job={job.def?.defName ?? "null"}, forced={job.playerForced}, " +
                $"workGiverDef={job.workGiverDef?.defName ?? "null"}, " +
                $"sourceNode={sourceType?.FullName ?? "null"}, " +
                $"sourceAssembly={sourceType?.Assembly.GetName().Name ?? "null"}, " +
                $"targetA={DescribeTarget(job.targetA)}, targetB={DescribeTarget(job.targetB)}, " +
                $"targetC={DescribeTarget(job.targetC)}.");
        }

        private static string DescribeTarget(LocalTargetInfo target)
        {
            if (!target.IsValid)
                return "invalid";

            if (target.HasThing && target.Thing != null)
            {
                Thing thing = target.Thing;
                return $"Thing({thing.def?.defName ?? "null"}#{thing.thingIDNumber}@{thing.Position}, type={thing.GetType().FullName})";
            }

            return $"Cell({target.Cell})";
        }
    }
}
