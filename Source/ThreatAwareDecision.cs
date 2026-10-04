using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    // Records references and primitive values only. Text is formatted when the inspect pane
    // asks for it, never inside a WorkGiver candidate loop.
    internal static class ThreatAwareDecision
    {
        private sealed class Entry
        {
            internal Map map;
            internal IntVec3 target;
            internal Thing threat;
            internal JobDef job;
            internal WorkGiverDef giver;
            internal string reason;
            internal int tick;
        }
        private static ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();
        internal static void Reset() => entries = new ConditionalWeakTable<Pawn, Entry>();
        internal static void Remember(Pawn pawn, LocalTargetInfo target, Thing threat, string reason, Job job)
        {
            Entry entry = entries.GetOrCreateValue(pawn);
            entry.map = pawn.Map;
            entry.target = target.Cell;
            entry.threat = threat;
            entry.job = job?.def;
            entry.giver = null;
            entry.reason = reason;
            entry.tick = Find.TickManager?.TicksGame ?? 0;
        }
        internal static void SetWorkGiver(Pawn pawn, WorkGiverDef giver)
        {
            if (pawn != null && entries.TryGetValue(pawn, out Entry entry)) entry.giver = giver;
        }
        internal static void SetJob(Pawn pawn, Job job)
        {
            if (pawn != null && entries.TryGetValue(pawn, out Entry entry)) entry.job = job?.def;
        }
        internal static string Inspect(Pawn pawn)
        {
            if (!ThreatAwareOutdoorWorkPatch.IsProtectedPlayerPawn(pawn)
                || BetterRimAIMod.Settings?.threatAwareOutdoorWork != true || pawn.Map == null) return null;
            Area_Home home = pawn.Map.areaManager?.Home;
            if (home == null) return null;
            if (!ThreatAwareHomeSafety.IsSafeCell(pawn.Map, home, pawn.Position))
                return "BetterRimAI: Outside protected area; return toward safety is allowed";
            if (!entries.TryGetValue(pawn, out Entry entry) || entry.map != pawn.Map) return null;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < entry.tick || tick - entry.tick > 600) return null;
            return "BetterRimAI: Outdoor work restricted\nRejected: " + (entry.job?.defName ?? entry.giver?.defName ?? "outdoor candidate")
                + " at " + entry.target + "\nThreat: " + (entry.threat?.LabelShort ?? "near departure")
                + "\nReason: " + entry.reason;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    internal static class ThreatAwareInspectPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Pawn __instance, ref string __result)
        {
            string line = ThreatAwareDecision.Inspect(__instance);
            if (line != null) __result = string.IsNullOrEmpty(__result) ? line : __result + "\n" + line;
        }
    }
}
