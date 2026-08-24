using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Narrow diagnostics for RimWorld 1.6 JobGiver_Work. Unlike the earlier StartJob tracing,
    /// this logs only WorkGiver calls that actually return HaulToInventory or an already-known
    /// unsafe job, so it should produce one/few useful lines instead of per-tick spam.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), "GiverTryGiveJobPrioritized")]
    public static class ThreatAwareWorkGiverDiagnostics
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, WorkGiver giver, IntVec3 cell, Job __result)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatDebugLogging || pawn == null || giver == null || __result == null)
                return;

            bool haulToInventory = string.Equals(__result.def?.defName, "HaulToInventory", StringComparison.Ordinal);
            bool knownUnsafe = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, __result);
            if (!haulToInventory && !knownUnsafe)
                return;

            Type giverType = giver.GetType();
            Log.Message(
                $"[BetterRimAI][work-giver] pawn={pawn.LabelShort}, " +
                $"job={__result.def?.defName ?? "null"}, forced={__result.playerForced}, " +
                $"giverDef={giver.def?.defName ?? "null"}, giverType={giverType.FullName}, " +
                $"giverAssembly={giverType.Assembly.GetName().Name}, prioritizedCell={cell}, " +
                $"targetA={DescribeTarget(__result.targetA)}, targetB={DescribeTarget(__result.targetB)}, " +
                $"targetC={DescribeTarget(__result.targetC)}, knownUnsafe={knownUnsafe}.");
        }

        private static string DescribeTarget(LocalTargetInfo target)
        {
            if (!target.IsValid)
                return "invalid";

            if (target.HasThing && target.Thing != null)
            {
                Thing thing = target.Thing;
                return $"Thing({thing.def?.defName ?? "null"}#{thing.thingIDNumber}@{thing.Position})";
            }

            return $"Cell({target.Cell})";
        }
    }
}
