using HarmonyLib;
using Verse;
using Verse.AI;
namespace BetterRimAI
{
    /// <summary>Invalidate reused PawnPath objects at a path-start event, using direct access.</summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class ThreatAwarePathStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn ___pawn) => ThreatAwareOutdoorWorkPatch.InvalidatePath(___pawn);
    }
}
