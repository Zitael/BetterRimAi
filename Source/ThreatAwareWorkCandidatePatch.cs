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
    /// The path guard learns that a concrete work target is unsafe only after RimWorld has built
    /// a real path to it. Once learned, suppress that target while WorkGiver_Scanner is evaluating
    /// candidates. This is deliberately earlier than JobGiver_Work's final ThinkResult: rejecting
    /// the final result would make the pawn idle, while rejecting only the candidate lets vanilla
    /// keep scanning and choose the next useful job.
    /// </summary>
    [HarmonyPatch]
    public static class ThreatAwareWorkCandidatePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type scannerType = typeof(WorkGiver_Scanner);
            Assembly assembly = scannerType.Assembly;

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || !scannerType.IsAssignableFrom(type))
                {
                    continue;
                }

                MethodInfo thingMethod = AccessTools.DeclaredMethod(
                    type,
                    nameof(WorkGiver_Scanner.JobOnThing),
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (thingMethod != null && thingMethod.ReturnType == typeof(Job))
                {
                    yield return thingMethod;
                }

                MethodInfo cellMethod = AccessTools.DeclaredMethod(
                    type,
                    nameof(WorkGiver_Scanner.JobOnCell),
                    new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
                if (cellMethod != null && cellMethod.ReturnType == typeof(Job))
                {
                    yield return cellMethod;
                }
            }

            // Include the base implementations as well. Some scanners inherit them unchanged.
            MethodInfo baseThing = AccessTools.DeclaredMethod(
                scannerType,
                nameof(WorkGiver_Scanner.JobOnThing),
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (baseThing != null)
            {
                yield return baseThing;
            }

            MethodInfo baseCell = AccessTools.DeclaredMethod(
                scannerType,
                nameof(WorkGiver_Scanner.JobOnCell),
                new[] { typeof(Pawn), typeof(IntVec3), typeof(bool) });
            if (baseCell != null)
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

            if (ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, __result))
            {
                JobMaker.ReturnToPool(__result);
                __result = null;
            }
        }
    }

    /// <summary>
    /// Disable the older final-result suppression. It was useful to prove the block registry, but
    /// returning ThinkResult.NoJob after JobGiver_Work has already chosen a target prevents vanilla
    /// from considering its second-best candidate. Candidate filtering above replaces it.
    /// </summary>
    [HarmonyPatch(typeof(ThreatAwareOutdoorWorkPatch), nameof(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob))]
    public static class ThreatAwareLegacyFinalSuppressionCompatibilityPatch
    {
        // Marker patch only. The actual JobGiver_Work postfix is neutralized by making its caller
        // see no block after candidate scanning has had a chance to act. We cannot simply remove an
        // already compiled Harmony patch at runtime, so use a short-lived guard around final result
        // evaluation below.
    }

    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    [HarmonyPriority(Priority.Last)]
    public static class ThreatAwareFinalResultRecoveryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref ThinkResult __result)
        {
            // If the older postfix changed a valid unsafe result to NoJob, immediately ask the
            // normal work giver once more. Candidate-level patches above now hide the unsafe target,
            // so this second pass can select another job. Recursion is guarded per thread.
            if (__result.IsValid || pawn == null || BetterRimAIMod.Settings == null
                || !BetterRimAIMod.Settings.threatAwareOutdoorWork || RecoveryGuard.active)
            {
                return;
            }

            try
            {
                RecoveryGuard.active = true;
                ThinkResult retry = new JobGiver_Work().TryIssueJobPackage(pawn, default(JobIssueParams));
                if (retry.IsValid)
                {
                    __result = retry;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterRimAI] Alternative work recovery failed for " + pawn + ": " + ex);
            }
            finally
            {
                RecoveryGuard.active = false;
            }
        }

        private static class RecoveryGuard
        {
            [ThreadStatic]
            public static bool active;
        }
    }
}
