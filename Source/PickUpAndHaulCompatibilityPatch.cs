using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace BetterRimAI
{
    [HarmonyPatch]
    public static class PickUpAndHaulBlockedCandidatePatch
    {
        [HarmonyPrepare]
        public static bool Prepare()
        {
            MethodBase method = TargetMethod();
            if (method != null) Log.Message("[BetterRimAI][PUAH] compatibility enabled for " + method.DeclaringType.AssemblyQualifiedName);
            return method != null;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            return type == null ? null : AccessTools.DeclaredMethod(type, "HasJobOnThing", new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
        }

        // Positional binding works with both "thing" and "t" in third-party versions.
        [HarmonyPrefix]
        public static bool Prefix(Pawn __0, Thing __1, bool __2, ref bool __result)
        {
            if (!ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(__0, __1, __2, false, out string reason)) return true;
            __result = false;
            ThreatAwareBlockDiagnostics.Once("puah-rejected", __0, __1, null, true, "candidate rejected before movement");
            ThreatAwareRuntimeTrace.CandidateRejected(__0, "PUAH HasJobOnThing", __1,
                "PickUpAndHaul.WorkGiver_HaulToInventory", false, reason);
            return false;
        }
    }
}
