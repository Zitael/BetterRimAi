using UnityEngine;
using Verse;

namespace BetterRimAI
{
    public sealed class BetterRimAIMod : Mod
    {
        public static BetterRimAISettings Settings { get; private set; }

        public BetterRimAIMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BetterRimAISettings>();
        }

        public override string SettingsCategory()
        {
            return "Better Rim AI";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings ??= GetSettings<BetterRimAISettings>();

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"Long trip distance: {Settings.longTripDistance:F0} cells");
            Settings.longTripDistance = listing.Slider(Settings.longTripDistance, 20f, 200f);

            listing.Gap();
            listing.Label($"Eat before long trip below: {Settings.foodPrepareThreshold:P0}");
            Settings.foodPrepareThreshold = listing.Slider(Settings.foodPrepareThreshold, 0.20f, 0.80f);

            listing.Gap();
            listing.Label($"Rest before long trip below: {Settings.restPrepareThreshold:P0}");
            Settings.restPrepareThreshold = listing.Slider(Settings.restPrepareThreshold, 0.20f, 0.80f);

            listing.Gap();
            listing.CheckboxLabeled(
                "Debug logging",
                ref Settings.debugLogging,
                "Write BetterRimAI decisions to the RimWorld log. Useful while testing the mod.");

            listing.GapLine();
            listing.Label("Defaults: 50 cells, food 45%, rest 40%.");

            listing.End();
        }
    }
}
