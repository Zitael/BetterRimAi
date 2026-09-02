using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class HarmonyCompatibilityRegressionTests
    {
        [Test]
        public void GenericWorkGiverPrefix_UsesHarmonyArgsArray_NotForeignParameterNames()
        {
            MethodInfo prefix = typeof(ThreatAwareBlockedThingCandidatePatch)
                .GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);

            Assert.That(prefix, Is.Not.Null);

            ParameterInfo[] parameters = prefix.GetParameters();
            Assert.That(parameters.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(object[])));
            Assert.That(parameters[0].Name, Is.EqualTo("__args"));

            Assert.That(parameters.Any(p => p.Name == "t"), Is.False,
                "Regression: binding a Harmony prefix to a concrete foreign parameter name can make PatchAll fail when another mod names that parameter differently.");
        }

        [Test]
        public void PickUpAndHaulCompatibility_HasNoCompileTimeDependencyOnPickUpAndHaulAssembly()
        {
            string[] referencedAssemblies = typeof(PickUpAndHaulBlockedCandidatePatch)
                .Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .ToArray();

            Assert.That(referencedAssemblies, Does.Not.Contain("PickUpAndHaul"),
                "Pick Up And Haul support must remain optional; BetterRimAI should load when PUAH is not installed.");
        }

        [Test]
        public void PickUpAndHaulCompatibility_ResolvesTargetByReflection()
        {
            MethodInfo prepare = typeof(PickUpAndHaulBlockedCandidatePatch)
                .GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static);
            MethodInfo targetMethod = typeof(PickUpAndHaulBlockedCandidatePatch)
                .GetMethod("TargetMethod", BindingFlags.Public | BindingFlags.Static);

            Assert.That(prepare, Is.Not.Null);
            Assert.That(targetMethod, Is.Not.Null);
            Assert.That(targetMethod.ReturnType, Is.EqualTo(typeof(MethodBase)));
        }
    }
}
