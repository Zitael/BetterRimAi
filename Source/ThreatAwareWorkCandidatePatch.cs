using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// Generic compatibility layer for vanilla and modded WorkGiver_Scanner implementations.
    /// This prefix is an extremely hot path, so it must stay allocation-free in the common case.
    /// </summary>
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
                if (type == null || type.IsAbstract || !scannerType.IsAssignableFrom(type))
                    continue;

                MethodInfo method = AccessTools.DeclaredMethod(type,
                    nameof(WorkGiver_Scanner.HasJobOnThing),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

                if (method != null && method.ReturnType == typeof(bool) && seen.Add(method))
                    yield return method;
            }

            MethodInfo baseMethod = AccessTools.DeclaredMethod(scannerType,
                nameof(WorkGiver_Scanner.HasJobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

            if (baseMethod != null && seen.Add(baseMethod))
                yield return baseMethod;
        }

        [HarmonyPrefix]
        public static bool Prefix(object[] __args, MethodBase __originalMethod, ref bool __result)
        {
            if (__args == null || __args.Length < 2)
                return true;

            Pawn pawn = __args[0] as Pawn;
            Thing thing = __args[1] as Thing;
            if (pawn == null || thing == null)
                return true;

            bool forced = __args.Length >= 3 && __args[2] is bool value && value;
            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkTarget(pawn, thing, forced))
                return true;

            ThreatAwareBlockDiagnostics.Once(
                "generic-rejected", pawn, thing, pawn.CurJob, true,
                "method=" + (__originalMethod?.DeclaringType?.FullName ?? "unknown") + "." + (__originalMethod?.Name ?? "unknown"));

            __result = false;
            return false;
        }
    }
}
