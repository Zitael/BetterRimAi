using System;
using Verse;
namespace BetterRimAI
{
    internal readonly struct ThreatTargetKey : IEquatable<ThreatTargetKey>
    {
        private readonly int mapId, thingId;
        private readonly string jobDef;
        private readonly IntVec3 cell;
        internal ThreatTargetKey(int mapId, int thingId, string jobDef, IntVec3 cell)
        {
            this.mapId = mapId;
            this.thingId = thingId;
            this.jobDef = thingId >= 0 ? null : jobDef;
            this.cell = thingId >= 0 ? IntVec3.Invalid : cell;
        }
        public bool Equals(ThreatTargetKey other) => mapId == other.mapId && thingId == other.thingId && jobDef == other.jobDef && cell == other.cell;
        public override bool Equals(object obj) => obj is ThreatTargetKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (((mapId * 397) ^ thingId) * 397 ^ (jobDef?.GetHashCode() ?? 0)) * 397 ^ cell.GetHashCode(); }
        }
    }
}
