using UnityEngine;
using Verse;

namespace BetterRimAI
{
    public sealed class BetterRimAISettings : ModSettings
    {
        public bool threatAwareOutdoorWork = true;
        public float routeThreatRadius = 15f;
        public float homeExitThreatRadius = 20f;
        public bool threatDebugLogging = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref threatAwareOutdoorWork, "threatAwareOutdoorWork", true);
            Scribe_Values.Look(ref routeThreatRadius, "routeThreatRadius", 15f);
            Scribe_Values.Look(ref homeExitThreatRadius, "homeExitThreatRadius", 20f);
            Scribe_Values.Look(ref threatDebugLogging, "threatDebugLogging", true);
            base.ExposeData();
        }
    }

    public sealed class BetterRimAIMod : Mod
    {
        public static BetterRimAISettings Settings = new BetterRimAISettings();

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
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "Threat-aware outdoor work",
                ref Settings.threatAwareOutdoorWork,
                "Colonists with Flee/Ignore hostility response will avoid automatic jobs outside the Home area when their actual route passes close to hostiles. Drafted, player-forced and Attack-response pawns are not restricted.");

            listing.GapLine();
            listing.Label($"Route threat radius: {Settings.routeThreatRadius:F0} cells");
            Settings.routeThreatRadius = listing.Slider(Settings.routeThreatRadius, 5f, 40f);
            listing.Label("How close a hostile may be to the calculated route before the outdoor job is blocked.");

            listing.Gap();
            listing.Label($"Home exit threat radius: {Settings.homeExitThreatRadius:F0} cells");
            Settings.homeExitThreatRadius = listing.Slider(Settings.homeExitThreatRadius, 5f, 40f);
            listing.Label("Extra safety radius around the point where the route leaves the Home area. This prevents pawns opening a base exit next to raiders, manhunters or shamblers.");

            listing.Gap();
            listing.CheckboxLabeled(
                "Debug threat decisions",
                ref Settings.threatDebugLogging,
                "Writes throttled BetterRimAI threat decisions to the RimWorld log.");

            listing.End();
        }
    }
}
