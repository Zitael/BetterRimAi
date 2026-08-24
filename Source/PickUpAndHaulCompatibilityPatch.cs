using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// Compatibility patch for Pick Up And Haul.
    /// We intentionally patch its concrete HasJobOnThing by reflection, without taking a compile-time
    /// dependency on PickUpAndHaul.dll. Once the runtime guard proves a Thing unsafe for this pawn,
    /// the PUAH WorkGiver must reject that Thing during its own candidate scan. This prevents the
    /// StartJob -> cancel -> immediately select the same haul target loop.
    /// </summary>
    [HarmonyPatch]
    public static class PickUpAndHaulBlockedCandidatePatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            return AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory") != null;
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
        public static bool Prefix(Pawn pawn, Thing thing, ref bool __result)
        {
            if (pawn == null || thing == null)
                return true;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return true;

            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkTarget(pawn, thing))
                return true;

            __result = false;
            return false;
        }
    }
}
