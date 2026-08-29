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

            Assert.That(safety.Contains("flood-filling non-Home cells from the map edge", StringComparison.Ordinal), Is.True,
                "Protected-base semantics must include enclosed holes in the Home-area paint.");
            Assert.That(outdoor.Contains("ThreatAwareHomeSafety.IsSafeCell", StringComparison.Ordinal), Is.True,
                "Movement route checks must use the protected base envelope, not raw Home cells.");
            Assert.That(cooldown.Contains("ThreatAwareHomeSafety.IsSafeCell", StringComparison.Ordinal), Is.True,
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

            Assert.That(method.Contains("pawn.Faction != Faction.OfPlayer", StringComparison.Ordinal), Is.True,
                "Colonists, player mechs/militors and player-faction drones should share the threat guard.");
            Assert.That(method.Contains("pawn.RaceProps.Animal", StringComparison.Ordinal), Is.True,
                "Ordinary tame animals should not be pulled into colonist/mech job filtering.");
            Assert.That(method.Contains("pawn.IsColonist", StringComparison.Ordinal), Is.False,
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
