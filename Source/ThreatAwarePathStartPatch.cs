using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// PatherTick deliberately throttles expensive threat scans. PawnPath instances, however,
    /// can be reused by RimWorld, so reference equality alone is not a reliable indication that
    /// the pawn started a new job/path. Invalidate BetterRimAI's cached path state whenever
    /// StartPath is called so the very next PatherTick performs a full safety check.
    ///
    /// Reflection here is acceptable: StartPath runs only when a path is started, not every tick.
    /// All FieldInfo objects are resolved once.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class ThreatAwarePathStartPatch
    {
        private static readonly FieldInfo CheckStateByPawnField;
        private static readonly FieldInfo StatePathField;
        private static readonly FieldInfo StateLastCheckTickField;
        private static readonly FieldInfo HostileCacheField;
        private static readonly bool Ready;

        static ThreatAwarePathStartPatch()
        {
            try
            {
                Type outdoorType = typeof(ThreatAwareOutdoorWorkPatch);
                CheckStateByPawnField = outdoorType.GetField("CheckStateByPawn", BindingFlags.NonPublic | BindingFlags.Static);
                HostileCacheField = outdoorType.GetField("HostileCache", BindingFlags.NonPublic | BindingFlags.Static);

                Type stateType = outdoorType.GetNestedType("PathCheckState", BindingFlags.NonPublic);
                StatePathField = stateType?.GetField("path", BindingFlags.Public | BindingFlags.Instance);
                StateLastCheckTickField = stateType?.GetField("lastCheckTick", BindingFlags.Public | BindingFlags.Instance);

                Ready = CheckStateByPawnField != null && StatePathField != null && StateLastCheckTickField != null;
            }
            catch
            {
                Ready = false;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn)
        {
            if (!Ready || ___pawn == null)
                return;

            try
            {
                // Force the next PatherTick to treat this as a fresh path even if RimWorld reused
                // the same PawnPath object from a previous job.
                IDictionary states = CheckStateByPawnField.GetValue(null) as IDictionary;
                object state = states?[___pawn.thingIDNumber];
                if (state != null)
                {
                    StatePathField.SetValue(state, null);
                    StateLastCheckTickField.SetValue(state, -999999);
                }

                // A new path should also see hostiles that appeared since the previous cached scan.
                // Remove only this pawn's cache entry; rebuilding it once is cheap and avoids a
                // short unsafe window after a raid/manhunter appears.
                IDictionary hostileCache = HostileCacheField?.GetValue(null) as IDictionary;
                if (hostileCache != null && ___pawn.Map != null)
                {
                    long key = ((long)___pawn.Map.uniqueID << 32) | (uint)___pawn.thingIDNumber;
                    hostileCache.Remove(key);
                }
            }
            catch (Exception ex)
            {
                if (BetterRimAIMod.Settings?.threatDebugLogging == true)
                    Log.Warning("[BetterRimAI] Failed to invalidate threat path cache on StartPath: " + ex.Message);
            }
        }
    }
}
