using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwareRetryCooldownRegressionTests
    {
        [Test]
        public void UnsafeCancellationBackoff_IsInstalledBeforeJobOverride()
        {
            Type type = typeof(ThreatAwareOutdoorRetryCooldown);
            HarmonyPatch patch = type.GetCustomAttribute<HarmonyPatch>();
            Assert.That(patch, Is.Not.Null);

            MethodInfo prefix = type.GetMethod("BeforeUnsafeJobCancellation", BindingFlags.Public | BindingFlags.Static);
            Assert.That(prefix, Is.Not.Null);
            Assert.That(prefix.GetCustomAttribute<HarmonyPrefix>(), Is.Not.Null,
                "Cooldown must begin before CancelUnsafeCurrentJob calls CheckForJobOverride.");
        }

        [Test]
        public void RetryCooldown_IsLongEnoughToPreventBoundaryThrashing()
        {
            FieldInfo field = typeof(ThreatAwareOutdoorRetryCooldown)
                .GetField("RetryCooldownTicks", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            Assert.That((int)field.GetRawConstantValue(), Is.GreaterThanOrEqualTo(120));
        }

        [Test]
        public void ThinkNodeChecksOutdoorBackoffBeforeKnownTargetBlock()
        {
            string source = File.ReadAllText(FindSourceFile("ThreatAwareThinkNodePatch.cs"));
            int cooldown = source.IndexOf("ShouldSuppressOutdoorRetry", StringComparison.Ordinal);
            int targetBlock = source.IndexOf("ShouldSuppressWorkJob", StringComparison.Ordinal);

            Assert.That(cooldown, Is.GreaterThanOrEqualTo(0));
            Assert.That(targetBlock, Is.GreaterThan(cooldown),
                "After an unsafe cancellation, broad outdoor backoff must stop target-churn before expensive block revalidation.");
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
