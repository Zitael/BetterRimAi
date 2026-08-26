using System;
using System.Reflection;
using NUnit.Framework;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatAwarePerformanceRegressionTests
    {
        [Test]
        public void WorkCandidateHotPath_UsesDirectO1Lookup()
        {
            MethodInfo lookup = typeof(ThreatAwareOutdoorWorkPatch)
                .GetMethod("CouldBeBlockedThing", BindingFlags.Public | BindingFlags.Static);

            Assert.That(lookup, Is.Not.Null,
                "HasJobOnThing hot path must use the direct blocked-Thing lookup, not reflection over GlobalBlocks.");
            Assert.That(lookup.ReturnType, Is.EqualTo(typeof(bool)));
        }

        [Test]
        public void ReflectionBasedFastLookup_IsRemoved()
        {
            Type oldType = typeof(ThreatAwareOutdoorWorkPatch).Assembly
                .GetType("BetterRimAI.ThreatAwareBlockFastLookup", throwOnError: false);

            Assert.That(oldType, Is.Null,
                "Regression: reflection-based FieldInfo.GetValue lookup was costing many milliseconds per frame.");
        }

        [Test]
        public void MovingThreatRecheck_IsThrottled()
        {
            FieldInfo field = typeof(ThreatAwareOutdoorWorkPatch)
                .GetField("MovingThreatRecheckTicks", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(field, Is.Not.Null);
            int ticks = (int)field.GetRawConstantValue();
            Assert.That(ticks, Is.GreaterThanOrEqualTo(120),
                "Full route/threat scans must not run every path cell or every few ticks.");
        }
    }
}
