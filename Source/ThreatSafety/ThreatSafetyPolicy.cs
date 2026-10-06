using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// The single answer threat safety gives: would performing this autonomous work require a
    /// protected pawn to step into unsafe exterior space? Work is judged by the cells vanilla
    /// would perform it from (Touch uses vanilla's own touch rule), never by target coordinates.
    /// </summary>
    internal static class ThreatSafetyPolicy
    {
        // Largest Thing footprint (expanded by one cell) that is evaluated; bigger ones go to vanilla.
        private const int MaxWorkCells = 256;
        private static readonly int[] workCells = new int[MaxWorkCells];

        /// <summary>Cheap per-pawn eligibility. Player overrides always bypass threat safety.</summary>
        internal static bool Eligible(Pawn pawn)
        {
            if (BetterRimAIMod.Settings?.threatAwareOutdoorWork != true) return false;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Faction != Faction.OfPlayer) return false;
            // Colonists, slaves, colony mechs and modded work-capable player pawns; never animals.
            if (pawn.RaceProps == null || pawn.RaceProps.Animal) return false;
            if (pawn.Drafted) return false;
            Pawn_PlayerSettings settings = pawn.playerSettings;
            return settings == null || !settings.UsesConfigurableHostilityResponse
                   || settings.hostilityResponse != HostilityResponseMode.Attack;
        }

        /// <summary>
        /// Classifies one work target. <paramref name="safeCell"/> is set for
        /// <see cref="Verdict.SafeSide"/>: the nearest standable protected cell from which
        /// vanilla accepts the Touch.
        /// </summary>
        internal static Verdict Classify(MapThreatState state, Pawn pawn, int pawnComponent, LocalTargetInfo target,
            PathEndMode mode, out IntVec3 safeCell)
        {
            safeCell = IntVec3.Invalid;
            Map map = state.Map;
            if (!target.IsValid) return Verdict.NoWorkCell;
            Thing thing = target.HasThing ? target.Thing : null;
            if (thing != null && (!thing.Spawned || thing.Map != map)) return Verdict.NoWorkCell;
            IntVec3 cell = target.Cell;
            if (!cell.InBounds(map)) return Verdict.NoWorkCell;

            CellRect rect = thing != null ? thing.OccupiedRect() : CellRect.SingleCell(cell);
            // Hot path: indoor work far from the protected boundary.
            if (state.AllProtected(rect.ExpandedBy(1))) return Verdict.Interior;

            int count = CollectWorkCells(state, pawn, target, thing, rect, mode);
            if (count < 0) return Verdict.NoWorkCell;
            Verdict verdict = ThreatGeometry.Decide(workCells, count, pawnComponent, state.Component,
                state.IsProtected, state.Danger, state, out _);
            if (verdict == Verdict.SafeSide)
            {
                safeCell = NearestStandableProtected(state, pawn, pawnComponent, count);
                if (!safeCell.IsValid) verdict = Verdict.Interior;
            }
            return verdict;
        }

        private static int CollectWorkCells(MapThreatState state, Pawn pawn, LocalTargetInfo target, Thing thing,
            CellRect rect, PathEndMode mode)
        {
            Map map = state.Map;
            switch (mode)
            {
                case PathEndMode.OnCell:
                case PathEndMode.None:
                    workCells[0] = state.Index(target.Cell);
                    return 1;
                case PathEndMode.InteractionCell:
                    if (thing != null && thing.def.hasInteractionCell)
                    {
                        IntVec3 interaction = thing.InteractionCell;
                        if (!interaction.InBounds(map)) return -1;
                        workCells[0] = state.Index(interaction);
                        return 1;
                    }
                    break;
                case PathEndMode.ClosestTouch:
                    // Vanilla resolves ClosestTouch to standing on a standable single-cell target.
                    if (rect.minX == rect.maxX && rect.minZ == rect.maxZ && target.Cell.Standable(map))
                    {
                        workCells[0] = state.Index(target.Cell);
                        return 1;
                    }
                    break;
            }

            CellRect around = rect.ExpandedBy(1);
            if ((around.maxX - around.minX + 1) * (around.maxZ - around.minZ + 1) > MaxWorkCells) return -1;
            int count = 0;
            for (int z = around.minZ; z <= around.maxZ; z++)
                for (int x = around.minX; x <= around.maxX; x++)
                {
                    if (x < 0 || z < 0 || x >= state.Width || z >= state.Height) continue;
                    // Vanilla's Touch path stops at the first touching cell, i.e. next to the
                    // target, never on it; do the same.
                    if (rect.Contains(new IntVec3(x, 0, z))) continue;
                    int index = z * state.Width + x;
                    if (!state.Walkable[index]) continue;
                    // Vanilla's exact rule (adjacency, inside, diagonal corner restrictions),
                    // the same check job drivers use before doing Touch work.
                    if (!ReachabilityImmediate.CanReachImmediate(new IntVec3(x, 0, z), target, map, PathEndMode.Touch, pawn))
                        continue;
                    workCells[count++] = index;
                }
            return count;
        }

        private static IntVec3 NearestStandableProtected(MapThreatState state, Pawn pawn, int pawnComponent, int count)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestDistance = int.MaxValue;
            for (int k = 0; k < count; k++)
            {
                int index = workCells[k];
                if (state.Component[index] != pawnComponent) continue;
                IntVec3 c = new IntVec3(index % state.Width, 0, index / state.Width);
                if (!c.Standable(state.Map)) continue;
                int distance = (c - pawn.Position).LengthHorizontalSquared;
                if (distance < bestDistance)
                {
                    best = c;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Checks every place a completed work job sends the pawn: secondary targets (a haul's
        /// storage cell, a delivery's blueprint) and target queues (Pick Up And Haul pickups).
        /// </summary>
        internal static bool CompletedJobIsUnsafe(MapThreatState state, Pawn pawn, int pawnComponent, Job job,
            out LocalTargetInfo culprit, out Verdict verdict)
        {
            PathEndMode primaryMode = (job.workGiverDef?.Worker as WorkGiver_Scanner)?.PathEndMode ?? PathEndMode.Touch;
            culprit = job.targetA;
            verdict = Classify(state, pawn, pawnComponent, job.targetA, primaryMode, out _);
            if (verdict.IsReject()) return true;
            if (Secondary(state, pawn, pawnComponent, job, job.targetB, ref culprit, ref verdict)) return true;
            if (Secondary(state, pawn, pawnComponent, job, job.targetC, ref culprit, ref verdict)) return true;
            if (Queue(state, pawn, pawnComponent, job, job.targetQueueA, ref culprit, ref verdict)) return true;
            return Queue(state, pawn, pawnComponent, job, job.targetQueueB, ref culprit, ref verdict);
        }

        private static bool Queue(MapThreatState state, Pawn pawn, int pawnComponent, Job job,
            List<LocalTargetInfo> queue, ref LocalTargetInfo culprit, ref Verdict verdict)
        {
            if (queue == null) return false;
            for (int i = 0; i < queue.Count; i++)
                if (Secondary(state, pawn, pawnComponent, job, queue[i], ref culprit, ref verdict)) return true;
            return false;
        }

        private static bool Secondary(MapThreatState state, Pawn pawn, int pawnComponent, Job job,
            LocalTargetInfo target, ref LocalTargetInfo culprit, ref Verdict verdict)
        {
            // targetA was judged with its WorkGiver's end mode; BuildRoof repeats it as targetB.
            if (!target.IsValid || target == job.targetA) return false;
            Verdict v = Classify(state, pawn, pawnComponent, target,
                target.HasThing ? PathEndMode.Touch : PathEndMode.OnCell, out _);
            if (!v.IsReject()) return false;
            culprit = target;
            verdict = v;
            return true;
        }
    }
}
