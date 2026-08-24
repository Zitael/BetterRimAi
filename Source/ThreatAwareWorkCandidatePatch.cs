using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// RimWorld 1.6 JobGiver_Work calls HasJobOnThing while its internal Validator is deciding
    /// whether a scanner candidate may participate in the best-target search. If the runtime path
    /// guard has already proved a Thing unsafe, reject it here. Vanilla then keeps scanning instead
    /// of starting and cancelling the same hauling job over and over.
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
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (pawn == null || t == null)
                return true;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return true;

            // Global danger blocks prefer thingIDNumber when available, so a lightweight probe is
            // enough to ask whether this exact candidate was previously proven unsafe. The probe is
            // never started and never reserves anything.
            Job probe = JobMaker.MakeJob(JobDefOf.Wait);
            probe.targetA = t;
            bool suppress;
            try
            {
                suppress = ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, probe);
            }
            finally
            {
                JobMaker.ReturnToPool(probe);
            }

            if (!suppress)
                return true;

            __result = false;
            return false;
        }
    }
}
