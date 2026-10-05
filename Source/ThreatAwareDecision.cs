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
            internal bool fromPathGuard;
        }
        private static ConditionalWeakTable<Pawn, Entry> entries = new ConditionalWeakTable<Pawn, Entry>();
        internal static void Reset() => entries = new ConditionalWeakTable<Pawn, Entry>();
        internal static void Remember(Pawn pawn, LocalTargetInfo target, Thing threat, string reason, Job job)
        {
            Entry entry = entries.GetOrCreateValue(pawn);
            int tick = Find.TickManager?.TicksGame ?? 0;
            bool pathGuard = reason == "active path exposed";
            if (entry.fromPathGuard && !pathGuard && tick >= entry.tick && tick - entry.tick < 180)
                return;
            entry.map = pawn.Map;
            entry.target = target.Cell;
            entry.threat = threat;
            entry.job = job?.def;
            entry.giver = null;
            entry.reason = reason;
            entry.tick = tick;
            entry.fromPathGuard = pathGuard;
        }
        internal static void SetWorkGiver(Pawn pawn, WorkGiverDef giver)
        {
            if (pawn != null && entries.TryGetValue(pawn, out Entry entry) && !RecentGuard(entry)) entry.giver = giver;
        }
        internal static void SetJob(Pawn pawn, Job job)
        {
            if (pawn != null && entries.TryGetValue(pawn, out Entry entry) && !RecentGuard(entry)) entry.job = job?.def;
        }
        private static bool RecentGuard(Entry entry)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            return entry.fromPathGuard && tick >= entry.tick && tick - entry.tick < 180;
        }
        internal static string Inspect(Pawn pawn)
        {
            if (!ThreatAwareOutdoorWorkPatch.IsProtectedPlayerPawn(pawn)
                || BetterRimAIMod.Settings?.threatAwareOutdoorWork != true || pawn.Map == null) return null;
            Area_Home home = pawn.Map.areaManager?.Home;
            if (home == null) return null;
            if (!ThreatAwareHomeSafety.IsSafeCell(pawn.Map, home, pawn.Position))
                return "BetterRimAI: Outside protected area — return allowed";
            if (!entries.TryGetValue(pawn, out Entry entry) || entry.map != pawn.Map)
                return "BetterRimAI: No safety restriction";
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < entry.tick || tick - entry.tick > 3600)
                return "BetterRimAI: No safety restriction";
            return "BetterRimAI: BLOCKED — " + entry.reason
                + "\nJob: " + (entry.job?.defName ?? entry.giver?.defName ?? "outdoor candidate")
                + "\nThreat: " + (entry.threat?.LabelShort ?? "near departure")
                + "\nTarget: " + entry.target;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    internal static class ThreatAwareInspectPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Pawn __instance, ref string __result)
        {
            string line = ThreatAwareDecision.Inspect(__instance);
            if (line != null)
            {
                __result = string.IsNullOrEmpty(__result) ? line : line + "\n" + __result;
                ThreatAwareRuntimeTrace.UiRendered(__instance);
            }
        }
    }
}
