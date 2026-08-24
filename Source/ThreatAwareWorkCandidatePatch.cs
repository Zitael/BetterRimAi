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
    /// Hide thing targets that were already proven unsafe before JobGiver_Work chooses its best
    /// candidate. This lets vanilla continue scanning and pick the next useful task instead of
    /// selecting the unsafe target and then ending up with NoJob.
    ///
    /// GenTypes.AllTypes is intentional: hauling mods can provide their own WorkGiver_Scanner
    /// subclasses outside Assembly-CSharp, and those must be filtered too.
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
                {
                    continue;
                }

                MethodInfo method = AccessTools.DeclaredMethod(
                    type,
                    nameof(WorkGiver_Scanner.HasJobOnThing),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

                if (method != null && method.ReturnType == typeof(bool) && seen.Add(method))
                {
                    yield return method;
                }
            }

            MethodInfo baseMethod = AccessTools.DeclaredMethod(
                scannerType,
                nameof(WorkGiver_Scanner.HasJobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

            if (baseMethod != null && seen.Add(baseMethod))
            {
                yield return baseMethod;
            }
        }

        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!ShouldSuppressThing(pawn, t))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool ShouldSuppressThing(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null)
            {
                return false;
            }

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
            {
                return false;
            }

            // Thing-based danger blocks are identified by thingIDNumber, so the probe job's def is
            // irrelevant. It is never started or reserved.
            Job probe = JobMaker.MakeJob(JobDefOf.Wait);
            probe.targetA = thing;

            try
            {
                return ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, probe);
            }
            finally
            {
                JobMaker.ReturnToPool(probe);
            }
        }
    }

    /// <summary>
    /// Cell scanners need the actual generated Job because their block identity includes the job
    /// type as well as the destination cell. This also acts as a fallback for custom thing scanners
    /// whose HasJobOnThing implementation does not use the normal base behavior.
    /// </summary>
    [HarmonyPatch]
    public static class ThreatAwareBlockedGeneratedJobPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type scannerType = typeof(WorkGiver_Scanner);
            HashSet<MethodBase> seen = new HashSet<MethodBase>();

            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.IsAbstract || !scannerType.IsAssignableFrom(type))
                {
                    continue;
                }

                MethodInfo thingMethod = AccessTools.DeclaredMethod(
                    type,
                    nameof(WorkGiver_Scanner.JobOnThing),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (thingMethod != null && thingMethod.ReturnType == typeof(Job) && seen.Add(thingMethod))
                {
                    yield return thingMethod;
                }

                MethodInfo cellMethod = AccessTools.DeclaredMethod(
                    type,
                    nameof(WorkGiver_Scanner.JobOnCell),
                    new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
                if (cellMethod != null && cellMethod.ReturnType == typeof(Job) && seen.Add(cellMethod))
                {
                    yield return cellMethod;
                }
            }

            MethodInfo baseThing = AccessTools.DeclaredMethod(
                scannerType,
                nameof(WorkGiver_Scanner.JobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (baseThing != null && seen.Add(baseThing))
            {
                yield return baseThing;
            }

            MethodInfo baseCell = AccessTools.DeclaredMethod(
                scannerType,
                nameof(WorkGiver_Scanner.JobOnCell),
                new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
            if (baseCell != null && seen.Add(baseCell))
            {
                yield return baseCell;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null || pawn == null)
            {
                return;
            }

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
            {
                return;
            }

            if (!ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, __result))
            {
                return;
            }

            JobMaker.ReturnToPool(__result);
            __result = null;
        }
    }
}
