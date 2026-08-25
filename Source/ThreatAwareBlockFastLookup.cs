using System;
using System.Collections;
using System.Reflection;
using RimWorld;
using Verse;

namespace BetterRimAI
{
    /// <summary>
    /// Hot-path lookup used by WorkGiver_Scanner patches. Avoids constructing a probe Job for
    /// every candidate Thing. Reflection is resolved once at startup; individual checks are just
    /// a short list scan over BetterRimAI's own danger blocks.
    /// </summary>
    internal static class ThreatAwareBlockFastLookup
    {
        private static readonly FieldInfo GlobalBlocksField;
        private static readonly FieldInfo MapIdField;
        private static readonly FieldInfo ThingIdField;
        private static readonly bool Ready;

        static ThreatAwareBlockFastLookup()
        {
            try
            {
                Type patchType = typeof(ThreatAwareOutdoorWorkPatch);
                GlobalBlocksField = patchType.GetField("GlobalBlocks", BindingFlags.NonPublic | BindingFlags.Static);
                Type blockType = patchType.GetNestedType("GlobalDangerBlock", BindingFlags.NonPublic);
                MapIdField = blockType?.GetField("mapId", BindingFlags.Public | BindingFlags.Instance);
                ThingIdField = blockType?.GetField("thingId", BindingFlags.Public | BindingFlags.Instance);
                Ready = GlobalBlocksField != null && MapIdField != null && ThingIdField != null;
            }
            catch
            {
                Ready = false;
            }
        }

        public static bool CouldBeBlocked(Pawn pawn, Thing thing, bool forced)
        {
            if (!Ready || forced || pawn == null || thing == null || pawn.Map == null || pawn.Drafted)
                return false;

            IList blocks = GlobalBlocksField.GetValue(null) as IList;
            if (blocks == null || blocks.Count == 0)
                return false;

            int mapId = pawn.Map.uniqueID;
            int thingId = thing.thingIDNumber;
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                object block = blocks[i];
                if ((int)MapIdField.GetValue(block) == mapId && (int)ThingIdField.GetValue(block) == thingId)
                    return true;
            }
            return false;
        }
    }
}
