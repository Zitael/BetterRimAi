using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwareThinkNodeRegressionTests
    {
        [Test]
        public void UniversalFallback_PatchesThinkNodeJobGiver_NotSpecificModJob()
        {
            Type patch = typeof(ThreatAwareThinkNodePatch);
            object[] attributes = patch.GetCustomAttributes(inherit: false);

            bool targetsThinkNodeJobGiver = false;
            foreach (object attribute in attributes)
            {
                string text = attribute.ToString();
                if (text != null && text.Contains("HarmonyPatch"))
                {
                    targetsThinkNodeJobGiver = true;
                    break;
                }
            }

            Assert.That(targetsThinkNodeJobGiver, Is.True);
            Assert.That(File.ReadAllText(SourcePath("ThreatAwareThinkNodePatch.cs")),
                Does.Contain("typeof(ThinkNode_JobGiver)"),
                "Regression: fallback must stay generic and must not depend on a hygiene/well mod type.");
        }

        [Test]
        public void BlockedThinkResult_IsConvertedToNoJob_SoPriorityTreeCanContinue()
        {
            string source = File.ReadAllText(SourcePath("ThreatAwareThinkNodePatch.cs"));
            Assert.That(source, Does.Contain("ShouldSuppressWorkJob(pawn, job)"));
            Assert.That(source, Does.Contain("__result = ThinkResult.NoJob"),
                "Regression: cancelling after job start loops; filtering to NoJob lets parent ThinkNode try siblings such as sleep.");
        }

        [Test]
        public void ExplicitPlayerOrder_RemainsAuthoritative()
        {
            string source = File.ReadAllText(SourcePath("ThreatAwareThinkNodePatch.cs"));
            int forcedGuard = source.IndexOf("job.playerForced", StringComparison.Ordinal);
            int suppression = source.IndexOf("ShouldSuppressWorkJob", StringComparison.Ordinal);
            Assert.That(forcedGuard, Is.GreaterThanOrEqualTo(0));
            Assert.That(suppression, Is.GreaterThan(forcedGuard),
                "playerForced must bypass autonomous threat suppression.");
        }

        private static string SourcePath(string file)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && dir != null; i++, dir = Directory.GetParent(dir)?.FullName)
            {
                string candidate = Path.Combine(dir, "Source", file);
                if (File.Exists(candidate)) return candidate;
            }
            Assert.Fail("Could not locate Source/" + file + " from test directory.");
            return null;
        }
    }
}
