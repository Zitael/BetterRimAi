namespace BetterRimAI
{
    // Evidence expiry must revalidate, never blindly restart the same unsafe trip.
    internal sealed class ThreatRestriction
    {
        internal const int RecheckTicks = 120;
        internal bool Active { get; private set; }
        private int checkedAt;
        internal bool NeedsValidation(int tick) => tick < checkedAt || tick - checkedAt >= RecheckTicks;
        internal void Refresh(int tick, bool dangerPresent)
        {
            checkedAt = tick;
            Active = dangerPresent;
        }
    }
}
