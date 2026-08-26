using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    [HarmonyPatch]
    public static class ThreatAwareBlockedThingCandidatePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type scannerType = typeof(WorkGiver_Scanner);
            HashSet<MethodBase> seen = new HashSet<MethodBase>();
            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.IsAbstract || !scannerType.IsAssignableFrom(type)) continue;
                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.HasJobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (method != null && method.ReturnType == typeof(bool) && seen.Add(method)) yield return method;
            }
            MethodInfo baseMethod = AccessTools.DeclaredMethod(scannerType, nameof(WorkGiver_Scanner.HasJobOnThing), new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (baseMethod != null && seen.Add(baseMethod)) yield return baseMethod;
        }

        [HarmonyPrefix]
        public static bool Prefix(object[] __args, MethodBase __originalMethod, ref bool __result)
        {
            if (__args == null || __args.Length < 2) return true;
            Pawn pawn = __args[0] as Pawn;
            Thing thing = __args[1] as Thing;
            if (pawn == null || thing == null) return true;

            bool forced = __args.Length >= 3 && __args[2] is bool value && value;

            // This method can run hundreds of times per frame. The common case must remain O(1),
            // allocation-free, and reflection-free. Full threat validation only runs for a Thing
            // already present in BetterRimAI's tiny blocked-target set.
            if (!ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, thing, forced)) return true;

            Job probe = JobMaker.MakeJob(JobDefOf.Wait);
            probe.targetA = thing;
            bool suppress;
            try { suppress = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, probe); }
            finally { JobMaker.ReturnToPool(probe); }
            if (!suppress) return true;

            ThreatAwareBlockDiagnostics.Once("generic-rejected", pawn, thing, pawn.CurJob, true,
                "method=" + (__originalMethod?.DeclaringType?.FullName ?? "unknown") + "." + (__originalMethod?.Name ?? "unknown"));
            __result = false;
            return false;
        }
    }
}
