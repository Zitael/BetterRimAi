using UnityEngine;
using Verse;

namespace BetterRimAI
{
    public sealed class BetterRimAISettings : ModSettings
    {
        internal const float DefaultThreatRadius = 18f;

        public bool threatAwareOutdoorWork = true;
        public float threatRadius = DefaultThreatRadius;
        public bool threatDebugLogging = false;
        public bool remoteWorkLocality = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref threatAwareOutdoorWork, "threatAwareOutdoorWork", true);
            Scribe_Values.Look(ref threatRadius, "threatRadius", DefaultThreatRadius);
            Scribe_Values.Look(ref threatDebugLogging, "threatDebugLogging", false);
            Scribe_Values.Look(ref remoteWorkLocality, "remoteWorkLocality", true);
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
                "Don't leave the base for work during threats",
                ref Settings.threatAwareOutdoorWork,
                "While an active hostile threat makes the outside unsafe, colonists and player mechs inside the Home area do not pick autonomous work that requires stepping into that danger. Work that can be done from inside (for example repairing an outer wall from the inside) stays available. Pawns already outside, drafted pawns, direct orders and Attack-response colonists are never restricted. With no active threat this does nothing.");

            listing.GapLine();
            listing.Label($"Hostile danger radius: {Settings.threatRadius:F0} cells");
            Settings.threatRadius = listing.Slider(Settings.threatRadius, 6f, 40f);
            listing.Label("Walking distance around an active hostile that counts as unsafe. Walls and closed doors stop it. Hostile turrets use their real firing lanes instead.");

            listing.Gap();
            listing.CheckboxLabeled(
                "Debug threat decisions",
                ref Settings.threatDebugLogging,
                "Logs threat state changes, rejected candidates, safe-side paths, and one line per job started by the selected pawn showing whether BetterRimAI changed it.");

            listing.GapLine();
            listing.CheckboxLabeled(
                "Prefer nearby work at remote sites",
                ref Settings.remoteWorkLocality,
                "After a successful long-distance work trip, favor more nearby work of the same type without overriding higher priorities or critical needs.");

            listing.End();
        }
    }
}
