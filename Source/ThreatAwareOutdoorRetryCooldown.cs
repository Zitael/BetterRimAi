using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// Per-pawn outdoor restriction backed by actual unsafe-route evidence. Safe indoor work
    /// remains available. Revalidate on expiry instead of blindly retrying a dangerous trip.
    /// Weak pawn keys and map identity prevent state leaking between saves or map transfers.
    /// </summary>
    public static class ThreatAwareOutdoorRetryCooldown
    {
        private sealed class Entry
        {
            internal Map map;
            internal IntVec3 dangerCell;
            internal float radius;
            internal readonly ThreatRestriction restriction = new ThreatRestriction();
        }
        private static ConditionalWeakTable<Pawn, Entry> Entries = new ConditionalWeakTable<Pawn, Entry>();

        internal static void Reset() => Entries = new ConditionalWeakTable<Pawn, Entry>();

        internal static void Remember(Pawn pawn, IntVec3 dangerCell, float radius, int tick)
        {
            Entry entry = Entries.GetOrCreateValue(pawn);
            entry.map = pawn.Map;
            entry.dangerCell = dangerCell;
            entry.radius = radius;
            entry.restriction.Refresh(tick, true);
        }

        internal static bool Applies(Pawn pawn, bool forced)
        {
            if (forced || pawn == null || pawn.Map == null || pawn.Drafted
                || BetterRimAIMod.Settings?.threatAwareOutdoorWork != true) return false;
            return ThreatAwareOutdoorWorkPatch.IsProtectedPlayerPawn(pawn)
                && !(pawn.playerSettings != null && pawn.playerSettings.UsesConfigurableHostilityResponse
                    && pawn.playerSettings.hostilityResponse == HostilityResponseMode.Attack);
        }

        internal static bool IsRestricted(Pawn pawn)
        {
            if (!Entries.TryGetValue(pawn, out Entry entry)) return false;
            if (!ReferenceEquals(entry.map, pawn.Map))
            {
                Entries.Remove(pawn);
                return false;
            }
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (entry.restriction.NeedsValidation(tick))
            {
                bool present = ThreatAwareOutdoorWorkPatch.DangerStillPresent(pawn, entry.dangerCell, entry.radius, tick);
                entry.restriction.Refresh(tick, present);
                if (!present)
                {
                    Entries.Remove(pawn);
                    ThreatAwareBlockDiagnostics.Once("restriction-cleared", pawn, null, null, false);
                }
            }
            return entry.restriction.Active;
        }

        public static bool ShouldSuppressOutdoorRetry(Pawn pawn, Job job)
        {
            return job != null && Applies(pawn, job.playerForced)
                && IsRestricted(pawn) && JobHasTargetOutsideHome(pawn, job);
        }

        internal static bool JobHasTargetOutsideHome(Pawn pawn, Job job)
        {
            Map map = pawn?.Map;
            Area_Home home = map?.areaManager?.Home;
            if (home == null || job == null) return false;
            if (TargetIsOutsideHome(job.targetA, map, home)
                || TargetIsOutsideHome(job.targetB, map, home)
                || TargetIsOutsideHome(job.targetC, map, home)) return true;
            // Only completed jobs have queues; scanner candidates stay O(1).
            // PUAH may add outdoor pickups to an indoor primary target.
            if (job.targetQueueA != null)
                for (int i = 0; i < job.targetQueueA.Count; i++)
                    if (TargetIsOutsideHome(job.targetQueueA[i], map, home)) return true;
            if (job.targetQueueB != null)
                for (int i = 0; i < job.targetQueueB.Count; i++)
                    if (TargetIsOutsideHome(job.targetQueueB[i], map, home)) return true;
            return false;
        }

        internal static bool TargetIsOutsideHome(LocalTargetInfo target, Map map, Area_Home home)
        {
            if (!target.IsValid || home == null) return false;
            if (target.HasThing && (target.Thing == null || target.Thing.Map != map)) return false;
            IntVec3 cell = target.Cell;
            return cell.IsValid && cell.InBounds(map) && !ThreatAwareHomeSafety.IsSafeCell(map, home, cell);
        }
    }
}
