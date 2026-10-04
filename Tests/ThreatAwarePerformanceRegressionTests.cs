using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;
using Verse.AI;

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
        public void ThreatGuard_DoesNotPatchPatherTick()
        {
            HarmonyPatch[] patches = typeof(ThreatAwareOutdoorWorkPatch)
                .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .Cast<HarmonyPatch>()
                .ToArray();

            string combined = string.Join(" ", patches.Select(p => p.info?.methodName ?? string.Empty));
            Assert.That(combined, Does.Not.Contain(nameof(Pawn_PathFollower.PatherTick)),
                "PatherTick runs dozens/hundreds of times per frame; threat guard must stay off that hot path.");
            Assert.That(combined, Does.Contain("TryEnterNextPathCell"),
                "Threat guard should run when a pawn actually advances to another path cell.");
        }

        [Test]
        public void ObsoletePerPawnThreatAndCooldownCachesAreRemoved()
        {
            var assembly = typeof(ThreatAwareOutdoorWorkPatch).Assembly;
            Assert.That(assembly.GetType("BetterRimAI.ThreatAwareOutdoorRetryCooldown"), Is.Null);
            Assert.That(assembly.GetType("BetterRimAI.ThreatTargetKey"), Is.Null);
            Assert.That(typeof(ThreatAwareOutdoorWorkPatch).GetField("HostileCache", BindingFlags.NonPublic | BindingFlags.Static), Is.Null);
        }
    }
}
