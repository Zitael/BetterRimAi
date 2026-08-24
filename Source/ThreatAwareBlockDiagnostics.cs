using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>Small deduplicated diagnostic logger for the threat-block handoff.</summary>
    public static class ThreatAwareBlockDiagnostics
    {
        private static readonly HashSet<string> Seen = new HashSet<string>();

        public static void Once(string stage, Pawn pawn, Thing thing, Job job, bool? blocked = null, string extra = null)
        {
            BetterRimAISettings settings = BetterRimAIMod.Settings;
            if (settings == null || !settings.threatDebugLogging) return;

            int pawnId = pawn?.thingIDNumber ?? -1;
            int thingId = thing?.thingIDNumber ?? -1;
            string jobDef = job?.def?.defName ?? "null";
            string key = stage + ":" + pawnId + ":" + thingId + ":" + jobDef + ":" + (blocked?.ToString() ?? "null") + ":" + (extra ?? "");
            if (!Seen.Add(key)) return;

            Log.Message($"[BetterRimAI][block-diag] stage={stage}, pawn={pawn?.LabelShort ?? "null"}#{pawnId}, " +
                        $"thing={thing?.LabelCap ?? "null"}#{thingId}@{(thing != null ? thing.Position.ToString() : "null")}, " +
                        $"job={jobDef}, blocked={(blocked.HasValue ? blocked.Value.ToString() : "n/a")}" +
                        (string.IsNullOrEmpty(extra) ? "." : $", {extra}."));
        }
    }
}
