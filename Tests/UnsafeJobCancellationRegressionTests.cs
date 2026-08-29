using System;
using System.IO;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class UnsafeJobCancellationRegressionTests
    {
        [Test]
        public void UnsafeCancellation_DoesNotMutateJobTrackerInsidePathing()
        {
            string source = File.ReadAllText(FindSourceFile("ThreatAwareOutdoorWorkPatch.cs"));
            int start = source.IndexOf("private static void CancelUnsafeCurrentJob", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = source.IndexOf("private static bool IsAttackOverride", start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            string method = StripLineComments(source.Substring(start, end - start));

            Assert.That(method.Contains("CheckForJobOverride()"), Is.False,
                "Pathing must never re-enter the think tree while TryEnterNextPathCell is on the stack.");
            Assert.That(method.Contains("EndCurrentJob("), Is.False,
                "Ending the current job from TryEnterNextPathCell can install another path before the old transition unwinds.");
            Assert.That(method.Contains("ThreatAwarePendingCancellation.Schedule(pawn, unsafeJob)"), Is.True,
                "Unsafe movement must be stopped immediately and cancellation deferred to Pawn_JobTracker.");
        }

        [Test]
        public void DeferredCancellation_RunsFromPawnJobTrackerTick()
        {
            string source = File.ReadAllText(FindSourceFile("ThreatAwarePendingCancellation.cs"));
            Assert.That(source.Contains("Pawn_JobTracker.JobTrackerTick"), Is.True);
            Assert.That(source.Contains("Pawn_JobTracker.JobTrackerTickInterval"), Is.True,
                "Performance mods may use interval ticking, so both tracker paths must consume pending cancellation.");
            Assert.That(source.Contains("EndCurrentJob(JobCondition.Incompletable, startNewJob: true)"), Is.True);
        }

        private static string StripLineComments(string source)
        {
            using (StringReader reader = new StringReader(source))
            using (StringWriter writer = new StringWriter())
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    writer.WriteLine(comment >= 0 ? line.Substring(0, comment) : line);
                }
                return writer.ToString();
            }
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
