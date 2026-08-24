using Verse;

namespace BetterRimAI
{
    public sealed class BetterRimAISettings : ModSettings
    {
        public float longTripDistance = 50f;
        public float foodPrepareThreshold = 0.45f;
        public float restPrepareThreshold = 0.40f;
        public bool debugLogging = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref longTripDistance, "longTripDistance", 50f);
            Scribe_Values.Look(ref foodPrepareThreshold, "foodPrepareThreshold", 0.45f);
            Scribe_Values.Look(ref restPrepareThreshold, "restPrepareThreshold", 0.40f);
            Scribe_Values.Look(ref debugLogging, "debugLogging", true);
            base.ExposeData();
        }
    }
}
