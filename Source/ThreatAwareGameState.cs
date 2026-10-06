using Verse;

namespace BetterRimAI
{
    // Runtime evidence is deliberately not saved. Fresh games/loads must not inherit caches,
    // log cooldowns or remote-work sessions from an earlier game in the same process.
    // (Class name kept so existing saves that list this component load without errors.)
    public sealed class ThreatAwareGameState : GameComponent
    {
        public ThreatAwareGameState(Game game)
        {
            ProtectedArea.Reset();
            MapThreatState.Reset();
            ThreatSafetyLog.Reset();
            RemoteWorkLocality.Reset();
        }
    }
}
