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
    /// v0.1: when vanilla has selected a normal work job far from the pawn, check whether
    /// food/rest are already low enough that the pawn is likely to interrupt the trip soon.
    /// If possible, replace the distant work with the corresponding vanilla need job.
    /// If vanilla cannot produce a need job yet, suppress the distant work for this think pass.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class LongTripNeedsPatch
    {
        private const int LogCooldownTicks = 600;

        private static readonly MethodInfo GetFoodJobMethod = AccessTools.Method(typeof(JobGiver_GetFood), "TryGiveJob");
        private static readonly MethodInfo GetRestJobMethod = AccessTools.Method(typeof(JobGiver_GetRest), "TryGiveJob");
        private static readonly Dictionary<int, int> LastLogTickByPawn = new Dictionary<int, int>();

        [HarmonyPostfix]
        public static void Postfix(JobGiver_Work __instance, Pawn pawn, ref ThinkResult __result)
        {
            try
            {
                if (__instance.emergency || pawn == null || !pawn.Spawned || !pawn.IsColonist || !__result.IsValid)
                {
                    return;
                }

                Job workJob = __result.Job;
                if (workJob == null || workJob.playerForced)
                {
                    return;
                }

                if (!TryGetDestination(workJob, out IntVec3 destination))
                {
                    return;
                }

                BetterRimAISettings settings = BetterRimAIMod.Settings ?? new BetterRimAISettings();

                float distance = pawn.Position.DistanceTo(destination);
                if (distance <= settings.longTripDistance)
                {
                    return;
                }

                Need_Food food = pawn.needs?.food;
                Need_Rest rest = pawn.needs?.rest;

                bool prepareFood = food != null && food.CurLevelPercentage <= settings.foodPrepareThreshold;
                bool prepareRest = rest != null && rest.CurLevelPercentage <= settings.restPrepareThreshold;

                if (!prepareFood && !prepareRest)
                {
                    return;
                }

                Job needJob = null;
                string reason = null;

                float foodUrgency = prepareFood
                    ? (settings.foodPrepareThreshold - food.CurLevelPercentage) / settings.foodPrepareThreshold
                    : -1f;
                float restUrgency = prepareRest
                    ? (settings.restPrepareThreshold - rest.CurLevelPercentage) / settings.restPrepareThreshold
                    : -1f;

                if (foodUrgency >= restUrgency && prepareFood)
                {
                    needJob = TryCreateVanillaNeedJob(GetFoodJobMethod, new JobGiver_GetFood(), pawn);
                    reason = "food";
                }

                if (needJob == null && prepareRest)
                {
                    needJob = TryCreateVanillaNeedJob(GetRestJobMethod, new JobGiver_GetRest(), pawn);
                    reason = "rest";
                }

                if (needJob == null && prepareFood && foodUrgency < restUrgency)
                {
                    needJob = TryCreateVanillaNeedJob(GetFoodJobMethod, new JobGiver_GetFood(), pawn);
                    reason = "food";
                }

                if (needJob != null)
                {
                    __result = new ThinkResult(needJob, __instance);
                    LogDecision(pawn, distance, food, rest, "replaced distant work with " + reason, settings);
                }
                else
                {
                    __result = ThinkResult.NoJob;
                    LogDecision(pawn, distance, food, rest, "deferred distant work; waiting for vanilla need job", settings);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BetterRimAI] Long-trip guard failed for " + pawn + ": " + ex);
            }
        }

        private static bool TryGetDestination(Job job, out IntVec3 destination)
        {
            if (job.targetA.IsValid)
            {
                destination = job.targetA.Cell;
                return destination.IsValid;
            }

            if (job.targetB.IsValid)
            {
                destination = job.targetB.Cell;
                return destination.IsValid;
            }

            destination = IntVec3.Invalid;
            return false;
        }

        private static Job TryCreateVanillaNeedJob(MethodInfo method, object giver, Pawn pawn)
        {
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(giver, new object[] { pawn }) as Job;
            }
            catch (TargetInvocationException ex)
            {
                Log.Warning("[BetterRimAI] Vanilla need job creation failed: " + (ex.InnerException ?? ex));
                return null;
            }
        }

        private static void LogDecision(
            Pawn pawn,
            float distance,
            Need_Food food,
            Need_Rest rest,
            string decision,
            BetterRimAISettings settings)
        {
            if (!settings.debugLogging)
            {
                return;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn.thingIDNumber;

            if (LastLogTickByPawn.TryGetValue(pawnId, out int lastTick) && tick - lastTick < LogCooldownTicks)
            {
                return;
            }

            LastLogTickByPawn[pawnId] = tick;
            string foodText = food == null ? "n/a" : food.CurLevelPercentage.ToString("P0");
            string restText = rest == null ? "n/a" : rest.CurLevelPercentage.ToString("P0");
            Log.Message($"[BetterRimAI] {pawn.LabelShort}: distant work {distance:F0} cells, food={foodText}, rest={restText} -> {decision}.");
        }
    }
}
