using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Verse.AI;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwareSafetyRegressionTests
    {
        [Test]
        public void ExplicitPlayerOrder_IsRecognizedAsForced()
        {
            MethodInfo method = typeof(ThreatAwareOutdoorWorkPatch).GetMethod(
                "IsPlayerForcedJob", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            var job = new Job { playerForced = true };
            Assert.That((bool)method.Invoke(null, new object[] { job }), Is.True,
                "Direct player orders must bypass BetterRimAI threat avoidance.");

            job.playerForced = false;
            Assert.That((bool)method.Invoke(null, new object[] { job }), Is.False,
                "Autonomous jobs must remain eligible for threat avoidance.");
        }

        [Test]
        public void ActualPathIsInspectedEvenWhenTargetIsInsideHome()
        {
            string sourcePath = FindSourceFile("ThreatAwareOutdoorWorkPatch.cs");
            string source = File.ReadAllText(sourcePath);

            int homeGuard = source.IndexOf("DestinationIsInsideHome(__instance.Destination, map, home)", StringComparison.Ordinal);
            int outsideCheck = source.IndexOf("bool leavesProtectedBase", StringComparison.Ordinal);
            int pathRead = source.IndexOf("PawnPath path = __instance.curPath", StringComparison.Ordinal);
            int oldBlock = source.IndexOf("state.blocked && JobMatchesStateBlock", StringComparison.Ordinal);

            Assert.That(homeGuard, Is.LessThan(0), "A home-side target can still have an exterior approach path.");
            Assert.That(pathRead, Is.GreaterThanOrEqualTo(0));
            Assert.That(outsideCheck, Is.GreaterThan(pathRead), "Use actual path cells before deciding whether the job leaves the protected area.");
            Assert.That(oldBlock, Is.GreaterThan(outsideCheck), "A previous unsafe approach must not cancel a new protected-side path.");
        }

        [Test]
        public void ForcedOrderGuard_RunsBeforePathAndThreatEvaluation()
        {
            string sourcePath = FindSourceFile("ThreatAwareOutdoorWorkPatch.cs");
            string source = File.ReadAllText(sourcePath);

            int forcedGuard = source.IndexOf("IsPlayerForcedJob(pawn.CurJob)", StringComparison.Ordinal);
            int pathRead = source.IndexOf("PawnPath path = __instance.curPath", StringComparison.Ordinal);

            Assert.That(forcedGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(pathRead, Is.GreaterThan(forcedGuard),
                "Direct player orders must bypass all BetterRimAI threat checks, including outdoor checks.");
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
