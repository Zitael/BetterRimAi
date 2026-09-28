using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>Rate limit by pawn and stage, before formatting target/job strings.</summary>
    public static class ThreatAwareBlockDiagnostics
    {
        private const int CooldownTicks = 600;
        private static readonly Dictionary<string, Dictionary<int, int>> LastByStage = new Dictionary<string, Dictionary<int, int>>();
        internal static void Reset() => LastByStage.Clear();

        public static void Once(string stage, Pawn pawn, Thing thing, Job job, bool? blocked = null, string extra = null)
        {
            if (BetterRimAIMod.Settings?.threatDebugLogging != true) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn?.thingIDNumber ?? -1;
            if (!LastByStage.TryGetValue(stage, out Dictionary<int, int> ticks))
            {
                ticks = new Dictionary<int, int>();
                LastByStage.Add(stage, ticks);
            }
            if (ticks.TryGetValue(pawnId, out int last) && tick >= last && tick - last < CooldownTicks) return;
            ticks[pawnId] = tick;
            Log.Message($"[BetterRimAI][block-diag] stage={stage}, pawn={pawn?.LabelShort ?? "null"}#{pawnId}, " +
                $"thing={thing?.LabelCap ?? "null"}#{thing?.thingIDNumber ?? -1}, job={job?.def?.defName ?? "null"}, " +
                $"blocked={blocked}, {extra ?? ""}.");
        }
    }
}
