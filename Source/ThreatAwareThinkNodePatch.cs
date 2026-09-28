using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>Fallback for completed autonomous jobs, including non-WorkGiver needs jobs.</summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareThinkNodePatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            // JobGiver_Work is a separate ThinkNode branch, not a ThinkNode_JobGiver.
            // Include its override explicitly without patching every priority/conditional node.
            var seen = new HashSet<MethodBase>();
            foreach (Type type in GenTypes.AllTypes)
            {
                if (type == null || type.ContainsGenericParameters
                    || (!typeof(ThinkNode_JobGiver).IsAssignableFrom(type) && !typeof(JobGiver_Work).IsAssignableFrom(type))) continue;
                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(ThinkNode_JobGiver.TryIssueJobPackage),
                    new[] { typeof(Pawn), typeof(JobIssueParams) });
                if (method != null && !method.IsAbstract && method.ReturnType == typeof(ThinkResult) && seen.Add(method))
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __0, ref ThinkResult __result)
        {
            Pawn pawn = __0;
            if (!__result.IsValid || pawn == null) return;
            Job job = __result.Job;
            if (job == null || job.playerForced) return;
            bool retryCooldown = ThreatAwareOutdoorRetryCooldown.ShouldSuppressOutdoorRetry(pawn, job);
            bool blockedTarget = !retryCooldown && ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job);
            if (!retryCooldown && !blockedTarget) return;
            ThreatAwareBlockDiagnostics.Once("candidate-rejected-before-movement", pawn,
                job.targetA.HasThing ? job.targetA.Thing : null, job, true, "ThinkNode fallback");
            __result = ThinkResult.NoJob;
        }
    }
}
