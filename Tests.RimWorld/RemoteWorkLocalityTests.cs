using System.Collections.Generic;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI.RimWorldTests
{
    [TestFixture, NonParallelizable]
    public class RemoteWorkLocalityTests
    {
        private HeadlessColony Colony;

        [SetUp]
        public void Setup() => Colony = new HeadlessColony();

        [TearDown]
        public void TearDown() => Colony.Dispose();

        private static bool HeadlessWorkPriority(WorkTypeDef __0, ref int __result)
        {
            __result = __0.defName == "urgent" ? 1 : __0.defName == "remote" ? 2 : 3;
            return false;
        }

        [Test]
        public void SuccessfulRemoteWorkTemporarilyFavorsSameGiverNearSite()
        {
            Pawn pawn = Colony.Pawn;
            pawn.needs = HeadlessColony.Bare<Pawn_NeedsTracker>();
            var giverDef = new WorkGiverDef();
            var giver = new WorkGiver_Repair { def = giverDef };
            HeadlessColony.Set(giverDef, "workerInt", giver);
            var remote = new IntVec3(70, 0, 5);
            var job = new Job { targetA = remote, workGiverDef = giverDef };
            RemoteWorkLocality.Started(pawn, job);
            HeadlessColony.Set(pawn, "positionInt", remote);
            RemoteWorkLocality.Finished(pawn, job, JobCondition.Succeeded);

            Assert.That(RemoteWorkLocality.TryActivate(pawn), Is.True);
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, new IntVec3(71, 0, 5)), Is.False);
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, HeadlessColony.Inside), Is.True);
            RemoteWorkLocality.Deactivate();
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, HeadlessColony.Inside), Is.False);
            HeadlessColony.Set(Colony.Ticks, "ticksGameInt", 2601);
            Assert.That(RemoteWorkLocality.TryActivate(pawn), Is.False);
        }

        [Test]
        public void CriticalFoodOrRestBelowTwentyPercentDisablesLocality()
        {
            Assert.That(RemoteWorkLocality.NeedsAllowLocality(0.19f, 1f), Is.False);
            Assert.That(RemoteWorkLocality.NeedsAllowLocality(1f, 0.19f), Is.False);
            Assert.That(RemoteWorkLocality.NeedsAllowLocality(0.2f, 0.2f), Is.True);
        }

        [Test]
        public void RemoteGiverMovesAheadOfLowerPriorityWorkOnly()
        {
            Pawn pawn = Colony.Pawn;
            pawn.needs = HeadlessColony.Bare<Pawn_NeedsTracker>();
            pawn.workSettings = HeadlessColony.Bare<Pawn_WorkSettings>();
            HeadlessColony.Set(pawn.workSettings, "pawn", pawn);
            var urgentDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "urgent" } };
            var remoteDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "remote" } };
            var trivialDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "trivial" } };
            var urgent = new WorkGiver_Repair { def = urgentDef };
            var remoteGiver = new WorkGiver_Repair { def = remoteDef };
            var trivial = new WorkGiver_Repair { def = trivialDef };
            HeadlessColony.Set(remoteDef, "workerInt", remoteGiver);
            var site = new IntVec3(70, 0, 5);
            var job = new Job { targetA = site, workGiverDef = remoteDef };
            RemoteWorkLocality.Started(pawn, job);
            HeadlessColony.Set(pawn, "positionInt", site);
            RemoteWorkLocality.Finished(pawn, job, JobCondition.Succeeded);
            var host = new Harmony("BetterRimAI.tests.locality-priority");
            var priority = AccessTools.Method(typeof(Pawn_WorkSettings), "GetPriority", new[] { typeof(WorkTypeDef) });
            host.Patch(priority, prefix: new HarmonyMethod(AccessTools.Method(typeof(RemoteWorkLocalityTests), nameof(HeadlessWorkPriority))));
            try
            {
                Assert.That(RemoteWorkLocality.TryActivate(pawn), Is.True);
                var order = new List<WorkGiver> { urgent, trivial, remoteGiver };
                RemoteWorkLocality.Prefer(pawn.workSettings, ref order);
                Assert.That(order, Is.EqualTo(new[] { urgent, remoteGiver, trivial }));
            }
            finally
            {
                RemoteWorkLocality.Deactivate();
                host.Unpatch(priority, HarmonyPatchType.All, host.Id);
            }
        }

        [Test]
        public void RemoteLocalityFiltersCandidatesThroughTheSharedJobGiverWorkFilter()
        {
            Pawn pawn = Colony.Pawn;
            pawn.needs = HeadlessColony.Bare<Pawn_NeedsTracker>();
            var giverDef = new WorkGiverDef();
            var giver = new WorkGiver_Repair { def = giverDef };
            HeadlessColony.Set(giverDef, "workerInt", giver);
            var remote = new IntVec3(70, 0, 5);
            var job = new Job { targetA = remote, workGiverDef = giverDef };
            RemoteWorkLocality.Started(pawn, job);
            HeadlessColony.Set(pawn, "positionInt", remote);
            RemoteWorkLocality.Finished(pawn, job, JobCondition.Succeeded);
            Assert.That(RemoteWorkLocality.TryActivate(pawn), Is.True);
            try
            {
                Thing farAway = Colony.MakeThing(30, HeadlessColony.Inside);
                Assert.That(WorkScanFilter.HasJobOnThing(giver, pawn, farAway, false), Is.False,
                    "too far from the remote site; rejected before the scanner runs");
            }
            finally { RemoteWorkLocality.Deactivate(); }
        }
    }
}
