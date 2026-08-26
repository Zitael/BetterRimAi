using System;
using System.IO;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class UnsafeJobCancellationRegressionTests
    {
        [Test]
        public void UnsafeCancellation_DoesNotReenterCheckForJobOverride()
        {
            string source = File.ReadAllText(FindSourceFile("ThreatAwareOutdoorWorkPatch.cs"));
            int start = source.IndexOf("private static void CancelUnsafeCurrentJob", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf("private static bool IsAttackOverride", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            string method = source.Substring(start, end - start);

            Assert.That(method.Contains("CheckForJobOverride()"), Is.False,
                "Calling CheckForJobOverride from TryEnterNextPathCell re-enters AI/pathing and can make pawns bounce between cells.");
            Assert.That(method.Contains("EndCurrentJob(JobCondition.Incompletable, startNewJob: true)"), Is.True,
                "Unsafe cancellation should hand replacement-job selection back to Pawn_JobTracker normally.");
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
