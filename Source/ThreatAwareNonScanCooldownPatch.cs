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
    /// RimWorld 1.6 can produce some work jobs (notably HaulToInventory) through
    /// WorkGiver_Scanner.NonScanJob. Those jobs bypass HasJobOnThing/JobOnThing, so the normal
    /// candidate filter never sees them. When the runtime path guard proves such a job unsafe,
    /// remember that work giver for the pawn for a short period. Returning null from NonScanJob
    /// lets JobGiver_Work continue scanning other work givers instead of reissuing the same job
    /// every tick.
    /// </summary>
    public static class ThreatAwareNonScanCooldown
    {
        // 30 in-game seconds at normal tick rate. If the threat is still present after this,
        // the path guard will stop the next attempt and refresh the cooldown.
        private const int CooldownTicks = 1800;

        private sealed class BlockedWork
        {
            public int mapId;
            public string jobDef;
            public string workGiverDef;
            public int untilTick;
        }

        private static readonly Dictionary<int, BlockedWork> BlockedByPawn = new Dictionary<int, BlockedWork>();

        public static void Remember(Pawn pawn, Job job)
        {
            if (pawn == null || pawn.Map == null || job == null)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            BlockedByPawn[pawn.thingIDNumber] = new BlockedWork
            {
                mapId = pawn.Map.uniqueID,
                jobDef = job.def?.defName,
                workGiverDef = job.workGiverDef?.defName,
                untilTick = tick + CooldownTicks
            };
        }

        public static bool ShouldSuppress(Pawn pawn, Job job)
        {
            if (pawn == null || pawn.Map == null || job == null || pawn.Drafted || IsAttackOverride(pawn))
                return false;

            if (!BlockedByPawn.TryGetValue(pawn.thingIDNumber, out BlockedWork blocked))
                return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (blocked.mapId != pawn.Map.uniqueID || tick >= blocked.untilTick)
            {
                BlockedByPawn.Remove(pawn.thingIDNumber);
                return false;
            }

            string workGiver = job.workGiverDef?.defName;
            if (!string.IsNullOrEmpty(blocked.workGiverDef) && !string.IsNullOrEmpty(workGiver))
                return string.Equals(blocked.workGiverDef, workGiver, StringComparison.Ordinal);

            return string.Equals(blocked.jobDef, job.def?.defName, StringComparison.Ordinal);
        }

        private static bool IsAttackOverride(Pawn pawn)
        {
            return pawn.playerSettings != null
                   && pawn.playerSettings.UsesConfigurableHostilityResponse
                   && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    [HarmonyPriority(Priority.First)]
    public static class ThreatAwareRememberUnsafeWorkPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            Job job = pawn?.CurJob;
            if (pawn == null || job == null)
                return;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return;

            // The path guard adds the global danger block immediately before ending the job, so
            // this is true specifically for a job we are ending because its route proved unsafe.
            if (ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job))
                ThreatAwareNonScanCooldown.Remember(pawn, job);
        }
    }

    [HarmonyPatch]
    public static class ThreatAwareBlockedNonScanJobPatch
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

                MethodInfo method = AccessTools.DeclaredMethod(type, nameof(WorkGiver_Scanner.NonScanJob),
                    new[] { typeof(Pawn) });
                if (method != null && method.ReturnType == typeof(Job) && seen.Add(method))
                    yield return method;
            }

            MethodInfo baseMethod = AccessTools.DeclaredMethod(scannerType, nameof(WorkGiver_Scanner.NonScanJob),
                new[] { typeof(Pawn) });
            if (baseMethod != null && seen.Add(baseMethod))
                yield return baseMethod;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null || pawn == null)
                return;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return;

            if (!ThreatAwareNonScanCooldown.ShouldSuppress(pawn, __result))
                return;

            JobMaker.ReturnToPool(__result);
            __result = null;
        }
    }
}
