using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    [HarmonyPatch]
    public static class ThreatAwareNonScanJobPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var seen = new HashSet<MethodBase>();
            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.ContainsGenericParameters || !typeof(WorkGiver).IsAssignableFrom(type)) continue;
                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(WorkGiver.NonScanJob), new[] { typeof(Pawn) });
                if (method != null && !method.IsAbstract && method.ReturnType == typeof(Job) && seen.Add(method)) yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __0, ref Job __result) => ThreatAwareCompletedWork.Filter(__0, false, ref __result);
    }

    // Some work scanners create jobs directly; also inspect secondary/queued destinations
    // after construction. Null lets JobGiver_Work continue to the next giver.
    [HarmonyPatch]
    public static class ThreatAwareThingJobPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods() => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.JobOnThing), typeof(Thing));
        [HarmonyPrefix]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, Thing __1, bool __2, ref Job __result)
        {
            if (!ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(__0, __1, __2,
                    __instance.PathEndMode == PathEndMode.Touch)) return true;
            __result = null;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", __0, __1, null, true, "JobOnThing");
            return false;
        }
        [HarmonyPostfix]
        public static void Postfix(WorkGiver_Scanner __instance, Pawn __0, bool __2, ref Job __result)
        {
            ThreatAwareCompletedWork.Filter(__0, __2, ref __result, __instance.PathEndMode == PathEndMode.Touch);
        }
    }

    [HarmonyPatch]
    public static class ThreatAwareCellJobPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods() => ThreatAwareScannerTargets.Find(nameof(WorkGiver_Scanner.JobOnCell), typeof(IntVec3));
        [HarmonyPrefix]
        public static bool Prefix(WorkGiver_Scanner __instance, Pawn __0, IntVec3 __1, bool __2, ref Job __result)
        {
            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressCandidate(__0, __1, __2,
                    __instance.PathEndMode == PathEndMode.Touch)) return true;
            __result = null;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", __0, null, null, true, "JobOnCell");
            return false;
        }
        [HarmonyPostfix]
        public static void Postfix(WorkGiver_Scanner __instance, Pawn __0, bool __2, ref Job __result)
        {
            ThreatAwareCompletedWork.Filter(__0, __2, ref __result, __instance.PathEndMode == PathEndMode.Touch);
        }
    }

    internal static class ThreatAwareCompletedWork
    {
        internal static void Filter(Pawn pawn, bool forced, ref Job job, bool safeTouch = false)
        {
            if (forced || job == null || !ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job, safeTouch)) return;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", pawn, null, job, true, "completed work candidate");
            job = null;
        }
    }
}
