using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>Finds a vanilla Touch destination on the protected side of a work target.</summary>
    internal static class ThreatAwareSafeWorkCell
    {
        internal static bool TryFind(Pawn pawn, LocalTargetInfo target, bool requireReachable, out IntVec3 best)
        {
            best = IntVec3.Invalid;
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !target.IsValid || !target.Cell.InBounds(map)) return false;

            CellRect rect = target.HasThing ? GenAdj.OccupiedRect(target.Thing) : CellRect.SingleCell(target.Cell);
            if (ThreatAwareHomeSafety.IsSafeCell(map, home, target.Cell))
            {
                bool exposedEdge = false;
                for (int z = rect.minZ - 1; z <= rect.maxZ + 1 && !exposedEdge; z++)
                    for (int x = rect.minX - 1; x <= rect.maxX + 1; x++)
                    {
                        IntVec3 edge = new IntVec3(x, 0, z);
                        if (edge.InBounds(map) && !ThreatAwareHomeSafety.IsSafeCell(map, home, edge))
                        {
                            exposedEdge = true;
                            break;
                        }
                    }
                if (!exposedEdge) return false;
            }
            int bestDistance = int.MaxValue;
            // Touch jobs may approach from any adjacent cell. Check the small perimeter only.
            for (int z = rect.minZ - 1; z <= rect.maxZ + 1; z++)
                for (int x = rect.minX - 1; x <= rect.maxX + 1; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map) || !ThreatAwareHomeSafety.IsSafeCell(map, home, cell)
                        || !cell.Standable(map) || cell.IsForbidden(pawn)) continue;
                    if (requireReachable && !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some)) continue;
                    int distance = (cell - pawn.Position).LengthHorizontalSquared;
                    if (distance < bestDistance)
                    {
                        best = cell;
                        bestDistance = distance;
                    }
                }
            return best.IsValid;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    internal static class ThreatAwareSafeWorkCellPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(Pawn ___pawn, ref LocalTargetInfo __0, ref PathEndMode __1)
        {
            if (__1 != PathEndMode.Touch || !ThreatAwareOutdoorPolicy.Applies(___pawn, false)
                || ___pawn.CurJob == null || ___pawn.CurJob.playerForced) return;
            Map map = ___pawn.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || !ThreatAwareHomeSafety.IsSafeCell(map, home, ___pawn.Position)) return;
            if (ThreatAwareSafeWorkCell.TryFind(___pawn, __0, true, out IntVec3 safe))
            {
                __0 = safe;
                __1 = PathEndMode.OnCell;
            }
        }
    }
}
