using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>A cached hostile combat source. Creation runs only on threat-snapshot refresh.</summary>
    internal readonly struct ThreatAwareThreat
    {
        internal readonly Thing Source;
        private readonly Verb attackVerb;
        private readonly float rangeSquared;
        internal bool IsRangedStructure => attackVerb != null;

        internal ThreatAwareThreat(Pawn pawn)
        {
            Source = pawn;
            attackVerb = null;
            rangeSquared = 0f;
        }

        private ThreatAwareThreat(Thing source, Verb verb)
        {
            Source = source;
            attackVerb = verb;
            float range = verb?.EffectiveRange ?? 0f;
            rangeSquared = range * range;
        }

        internal static bool TryCreateStructure(Pawn pawn, IAttackTarget target, out ThreatAwareThreat threat)
        {
            threat = default;
            Thing source = target?.Thing;
            if (source == null || source is Pawn || !source.Spawned || source.Destroyed
                || source.Map != pawn.Map || !source.HostileTo(pawn) || target.ThreatDisabled(pawn)) return false;
            Verb verb = (source as IAttackTargetSearcher)?.CurrentEffectiveVerb;
            if (source is Building_Turret && (verb == null || verb.IsMeleeAttack)) return false;
            if (verb != null && (!verb.Available() || verb.IsMeleeAttack)) return false;
            threat = new ThreatAwareThreat(source, verb);
            return true;
        }

        internal bool ThreatensCell(Map map, IntVec3 cell, float proximityRadius)
        {
            if (Source == null || !Source.Spawned || Source.Destroyed || Source.Map != map) return false;
            if (Source is Pawn pawn && (pawn.Dead || pawn.Downed)) return false;
            float distanceSquared = (cell - Source.Position).LengthHorizontalSquared;
            if (attackVerb == null) return distanceSquared <= proximityRadius * proximityRadius;
            if (distanceSquared > rangeSquared) return false;
            // Use the actual verb: range, minimum range and shoot-line/obstruction rules.
            return ThreatAwareFireCoverage.CanHit(Source, attackVerb, map, cell);
        }
    }

    /// <summary>Shared lazy firing geometry. Never called by a warm job-candidate check.</summary>
    internal static class ThreatAwareFireCoverage
    {
        private sealed class Entry
        {
            internal Map map;
            internal Verb verb;
            internal IntVec3 origin;
            internal int tick;
            internal byte[] cells;
        }
        private static ConditionalWeakTable<Thing, Entry> Entries = new ConditionalWeakTable<Thing, Entry>();
        internal static void Reset() => Entries = new ConditionalWeakTable<Thing, Entry>();

        internal static bool CanHit(Thing source, Verb verb, Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            int tick = Find.TickManager?.TicksGame ?? 0;
            Entry entry = Entries.GetOrCreateValue(source);
            int count = map.Size.x * map.Size.z;
            if (!ReferenceEquals(entry.map, map) || !ReferenceEquals(entry.verb, verb)
                || entry.origin != source.Position || tick < entry.tick
                || tick - entry.tick >= ThreatRestriction.RecheckTicks || entry.cells == null || entry.cells.Length != count)
            {
                entry.map = map;
                entry.verb = verb;
                entry.origin = source.Position;
                entry.tick = tick;
                if (entry.cells == null || entry.cells.Length != count) entry.cells = new byte[count];
                else Array.Clear(entry.cells, 0, entry.cells.Length);
            }
            int index = cell.z * map.Size.x + cell.x;
            byte value = entry.cells[index];
            if (value == 0)
            {
                value = verb.CanHitTargetFrom(source.Position, new LocalTargetInfo(cell)) ? (byte)2 : (byte)1;
                entry.cells[index] = value;
            }
            return value == 2;
        }
    }
}
