using System;
using System.IO;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwareProtectedBaseRegressionTests
    {
        [Test]
        public void EnclosedUnpaintedHomePockets_AreUsedEverywhereAsSafe()
        {
            string outdoor = File.ReadAllText(FindSourceFile("ThreatAwareOutdoorWorkPatch.cs"));
            string cooldown = File.ReadAllText(FindSourceFile("ThreatAwareOutdoorRetryCooldown.cs"));
            string safety = File.ReadAllText(FindSourceFile("ThreatAwareHomeSafety.cs"));

            Assert.That(safety.IndexOf("flood-filling non-Home cells from the map edge", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                "Protected-base semantics must include enclosed holes in the Home-area paint.");
            Assert.That(outdoor.IndexOf("ThreatAwareHomeSafety.IsSafeCell", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                "Movement route checks must use the protected base envelope, not raw Home cells.");
            Assert.That(cooldown.IndexOf("ThreatAwareHomeSafety.IsSafeCell", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                "Retry suppression must not classify an enclosed Home-area pocket as outdoors.");
        }

        [Test]
        public void ThreatGuard_CoversPlayerMechsAndModdedDrones_ButNotTameAnimals()
        {
            string source = File.ReadAllText(FindSourceFile("ThreatAwareOutdoorWorkPatch.cs"));
            int start = source.IndexOf("public static bool IsProtectedPlayerPawn", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf("public static bool CouldBeBlockedThing", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            string method = source.Substring(start, end - start);

            Assert.That(method.IndexOf("pawn.Faction != Faction.OfPlayer", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                "Colonists, player mechs/militors and player-faction drones should share the threat guard.");
            Assert.That(method.IndexOf("pawn.RaceProps.Animal", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0),
                "Ordinary tame animals should not be pulled into colonist/mech job filtering.");
            Assert.That(method.IndexOf("pawn.IsColonist", StringComparison.Ordinal), Is.LessThan(0),
                "A colonist-only gate would exclude player mechanoids and drones.");
        }

        private static string FindSourceFile(string fileName)
        {
            DirectoryInfo dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Source", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            Assert.Fail("Could not locate Source/" + fileName + " from test directory.");
            return null;
        }
    }
}
