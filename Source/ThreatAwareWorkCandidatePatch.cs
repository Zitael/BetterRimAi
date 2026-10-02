using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    internal static class ThreatAwareScannerTargets
    {
        // Startup only: include abstract intermediate overrides inherited by concrete scanners.
        internal static IEnumerable<MethodBase> Find(string name, Type targetType)
        {
            Type scanner = typeof(WorkGiver_Scanner);
            HashSet<MethodBase> seen = new HashSet<MethodBase>();
            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.ContainsGenericParameters || !scanner.IsAssignableFrom(type)) continue;
                if (type.FullName == "PickUpAndHaul.WorkGiver_HaulToInventory" && name == nameof(WorkGiver_Scanner.HasJobOnThing)) continue;
                MethodInfo method = AccessTools.DeclaredMethod(type, name, new[] { typeof(Pawn), targetType, typeof(bool) });
                Type resultType = name.StartsWith("Has", StringComparison.Ordinal) ? typeof(bool) : typeof(Job);
                if (method != null && !method.IsAbstract && method.ReturnType == resultType && seen.Add(method)) yield return method;
            }
        }
    }

    [HarmonyPatch]
    public static class ThreatAwareBlockedThingCandidatePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods() => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.HasJobOnThing), typeof(Thing));

        // Positional Harmony arguments avoid foreign parameter-name dependencies AND the
        // allocation/boxing required by object[] __args on every candidate.
        [HarmonyPrefix]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, Thing __1, bool __2, ref bool __result)
        {
            if (!ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(__0, __1, __2,
                    __instance.PathEndMode == PathEndMode.Touch)) return true;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", __0, __1, null, true, "HasJobOnThing");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    public static class ThreatAwareBlockedCellCandidatePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods() => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.HasJobOnCell), typeof(IntVec3));

        [HarmonyPrefix]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, IntVec3 __1, bool __2, ref bool __result)
        {
            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressCandidate(__0, __1, __2,
                    __instance.PathEndMode == PathEndMode.Touch)) return true;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", __0, null, null, true, "HasJobOnCell");
            __result = false;
            return false;
        }
    }
}
