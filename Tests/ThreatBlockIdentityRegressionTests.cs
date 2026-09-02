using System;
using System.Reflection;
using NUnit.Framework;
using Verse;

namespace BetterRimAI.Tests
{
    [TestFixture]
    public class ThreatBlockIdentityRegressionTests
    {
        private Type blockType;
        private MethodInfo blockMatchesJob;

        [SetUp]
        public void SetUp()
        {
            Type patchType = typeof(ThreatAwareOutdoorWorkPatch);
            blockType = patchType.GetNestedType("GlobalDangerBlock", BindingFlags.NonPublic);
            blockMatchesJob = patchType.GetMethod("BlockMatchesJob", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(blockType, Is.Not.Null);
            Assert.That(blockMatchesJob, Is.Not.Null);
        }

        [Test]
        public void ThingBackedBlock_MatchesSameThing_EvenIfProbeJobDefDiffers()
        {
            object block = MakeBlock(660784, "HaulToInventory", new IntVec3(148, 0, 136));

            bool matches = Invoke(block, 660784, "Wait", new IntVec3(148, 0, 136));

            Assert.That(matches, Is.True,
                "Regression: candidate probes use a lightweight Wait job; thingID must take precedence over jobDef.");
        }

        [Test]
        public void ThingBackedBlock_DoesNotMatchDifferentThing()
        {
            object block = MakeBlock(660784, "HaulToInventory", new IntVec3(148, 0, 136));

            bool matches = Invoke(block, 660785, "HaulToInventory", new IntVec3(148, 0, 136));

            Assert.That(matches, Is.False);
        }

        [Test]
        public void CellBackedBlock_FallsBackToJobDefAndDestination()
        {
            IntVec3 destination = new IntVec3(100, 0, 200);
            object block = MakeBlock(-1, "Mine", destination);

            Assert.That(Invoke(block, -1, "Mine", destination), Is.True);
            Assert.That(Invoke(block, -1, "ConstructFinishFrame", destination), Is.False);
            Assert.That(Invoke(block, -1, "Mine", new IntVec3(101, 0, 200)), Is.False);
        }

        private object MakeBlock(int thingId, string jobDef, IntVec3 destination)
        {
            object block = Activator.CreateInstance(blockType, nonPublic: true);
            blockType.GetField("thingId", BindingFlags.Public | BindingFlags.Instance).SetValue(block, thingId);
            blockType.GetField("jobDef", BindingFlags.Public | BindingFlags.Instance).SetValue(block, jobDef);
            blockType.GetField("destination", BindingFlags.Public | BindingFlags.Instance).SetValue(block, destination);
            return block;
        }

        private bool Invoke(object block, int thingId, string jobDef, IntVec3 destination)
        {
            return (bool)blockMatchesJob.Invoke(null, new[] { block, (object)thingId, jobDef, destination });
        }
    }
}
