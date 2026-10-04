using System;
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
        internal float Range => attackVerb == null ? 0f : (float)Math.Sqrt(rangeSquared);

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
                || source.Map != pawn.Map
                || !(source.HostileTo(pawn) || pawn.HostileTo(source) || source.HostileTo(pawn.Faction))
                || target.ThreatDisabled(pawn)) return false;
            Verb verb = (source as IAttackTargetSearcher)?.CurrentEffectiveVerb;
            if (source is Building_Turret && (verb == null || verb.IsMeleeAttack)) return false;
            if (verb != null && (!verb.Available() || verb.IsMeleeAttack)) return false;
            if (pawn.Map.generatorDef != null && !GenHostility.IsPotentialThreat(target)) return false;
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
            return attackVerb.CanHitTargetFrom(Source.Position, new LocalTargetInfo(cell));
        }
    }
}
