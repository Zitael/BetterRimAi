using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Short per-pawn backoff after BetterRimAI cancels an unsafe autonomous outdoor job.
    ///
    /// Without this, the think tree can immediately choose a different outdoor target (or the
    /// same modded need job) during CheckForJobOverride, making the pawn bounce at the Home edge.
    /// The cooldown only suppresses autonomous jobs that have a target outside Home; jobs inside
    /// Home and explicit player orders remain available. After a few seconds one outdoor job is
    /// allowed again and the normal route check decides whether conditions have become safe.
    /// </summary>
    [HarmonyPatch(typeof(ThreatAwareOutdoorWorkPatch), "CancelUnsafeCurrentJob")]
    public static class ThreatAwareOutdoorRetryCooldown
    {
        private const int RetryCooldownTicks = 180;
        private static readonly Dictionary<long, int> BlockUntilTick = new Dictionary<long, int>();

        // Prefix is intentional: CancelUnsafeCurrentJob calls CheckForJobOverride internally.
        // The backoff must already exist while that new job search is running.
        [HarmonyPrefix]
        public static void BeforeUnsafeJobCancellation(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            BlockUntilTick[PawnKey(pawn)] = tick + RetryCooldownTicks;
        }

        public static bool ShouldSuppressOutdoorRetry(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Map == null || job.playerForced || pawn.Drafted)
                return false;

            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatAwareOutdoorWork)
                return false;

            if (pawn.playerSettings != null
                && pawn.playerSettings.UsesConfigurableHostilityResponse
                && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack)
                return false;

            long key = PawnKey(pawn);
            if (!BlockUntilTick.TryGetValue(key, out int untilTick))
                return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick >= untilTick)
            {
                BlockUntilTick.Remove(key);
                return false;
            }

            return JobHasTargetOutsideHome(pawn, job);
        }

        internal static bool JobHasTargetOutsideHome(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null)
                return false;

            Area_Home home = pawn.Map.areaManager?.Home;
            if (home == null)
                return false;

            return TargetIsOutsideHome(job.targetA, pawn.Map, home)
                || TargetIsOutsideHome(job.targetB, pawn.Map, home)
                || TargetIsOutsideHome(job.targetC, pawn.Map, home);
        }

        private static bool TargetIsOutsideHome(LocalTargetInfo target, Map map, Area_Home home)
        {
            if (!target.IsValid)
                return false;

            IntVec3 cell;
            if (target.HasThing)
            {
                Thing thing = target.Thing;
                if (thing == null || thing.Map != map)
                    return false;
                cell = thing.Position;
            }
            else
            {
                cell = target.Cell;
            }

            return cell.IsValid && cell.InBounds(map) && !home[cell];
        }

        private static long PawnKey(Pawn pawn)
        {
            return ((long)pawn.Map.uniqueID << 32) | (uint)pawn.thingIDNumber;
        }
    }
}
