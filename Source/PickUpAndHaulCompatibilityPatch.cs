using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Optional compatibility for Pick Up And Haul. No compile-time dependency: if PUAH is absent,
    /// Harmony skips this patch and the generic BetterRimAI path remains active.
    /// </summary>
    [HarmonyPatch]
    public static class PickUpAndHaulBlockedCandidatePatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            Type type = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            if (type != null)
                Log.Message("[BetterRimAI][PUAH] compatibility enabled for " + type.AssemblyQualifiedName);
            return type != null;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            return type == null
                ? null
                : AccessTools.DeclaredMethod(type, "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Thing thing, bool forced, ref bool __result)
        {
            if (pawn == null || thing == null)
                return true;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return true;

            Job probe = JobMaker.MakeJob(JobDefOf.Wait);
            probe.targetA = thing;
            bool suppress;
            try
            {
                suppress = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, probe);
            }
            finally
            {
                JobMaker.ReturnToPool(probe);
            }

            ThreatAwareBlockDiagnostics.Once(
                "puah-hasjob",
                pawn,
                thing,
                pawn.CurJob,
                suppress,
                $"forced={forced}, curJob={pawn.CurJob?.def?.defName ?? "null"}");

            if (!suppress)
                return true;

            __result = false;
            ThreatAwareBlockDiagnostics.Once("puah-rejected", pawn, thing, pawn.CurJob, true, "HasJobOnThing forced false");
            return false;
        }
    }
}
