using Verse;

namespace BetterRimAI
{
    // Runtime evidence is deliberately not saved. Fresh games/loads must not inherit IDs,
    // paths, targets or cached Home envelopes from an earlier game in the same process.
    public sealed class ThreatAwareGameState : GameComponent
    {
        public ThreatAwareGameState(Game game)
        {
            ThreatAwareOutdoorWorkPatch.Reset();
            ThreatAwareHomeSafety.Reset();
            ThreatAwarePendingCancellation.Reset();
            ThreatAwareBlockDiagnostics.Reset();
            RemoteWorkLocality.Reset();
        }
    }
}
