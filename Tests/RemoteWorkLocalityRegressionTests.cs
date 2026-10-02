using System.Collections.Generic;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI.Tests
{
    public partial class CandidateSelectionTests
    {
        private sealed class TouchCellScanner : WorkGiver_Scanner
        {
            public override PathEndMode PathEndMode => PathEndMode.Touch;
        }

        private static bool HeadlessStandable(ref bool __result)
        {
            __result = true;
            return false;
        }

        private static bool HeadlessWorkPriority(WorkTypeDef __0, ref int __result)
        {
            __result = __0.defName == "urgent" ? 1 : __0.defName == "remote" ? 2 : 3;
            return false;
        }

        [Test]
        public void ExteriorConstructionRemainsAvailableFromProtectedSide()
        {
            PrepareCombatFixture();
            var host = new Harmony("BetterRimAI.tests.safe-side");
            var standable = AccessTools.Method(typeof(GenGrid), "Standable", new[] { typeof(IntVec3), typeof(Map) });
            host.Patch(standable, prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(HeadlessStandable))));
            try
            {
                var wall = new Building { def = Bare<ThingDef>() };
                wall.def.size = new IntVec2(1, 1);
                Set(wall, "positionInt", new IntVec3(6, 0, 5));
                Set(wall, "mapIndexOrState", (sbyte)0);
                Assert.That(ThreatAwareSafeWorkCell.TryFind(pawn, wall, false, out IntVec3 cell), Is.True);
                Assert.That(cell, Is.EqualTo(Inside));
                Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, wall, false, true), Is.False);

                Set(wall, "positionInt", new IntVec3(20, 0, 20));
                Assert.That(ThreatAwareSafeWorkCell.TryFind(pawn, wall, false, out _), Is.False);
                Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, wall, false, true), Is.True);
            }
            finally { host.Unpatch(standable, HarmonyPatchType.All, host.Id); }
        }

        [Test]
        public void TouchCellWorkAtBoundaryKeepsSafeSideCandidate()
        {
            PrepareCombatFixture();
            var host = new Harmony("BetterRimAI.tests.safe-touch-cell");
            var standable = AccessTools.Method(typeof(GenGrid), "Standable", new[] { typeof(IntVec3), typeof(Map) });
            host.Patch(standable, prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(HeadlessStandable))));
            try
            {
                bool result = true;
                Assert.That(ThreatAwareBlockedCellCandidatePatch.Prefix(new TouchCellScanner(), pawn,
                    new IntVec3(6, 0, 5), false, ref result), Is.True);
                result = true;
                Assert.That(ThreatAwareBlockedCellCandidatePatch.Prefix(new TouchCellScanner(), pawn,
                    new IntVec3(20, 0, 20), false, ref result), Is.False);
                Assert.That(result, Is.False);
            }
            finally { host.Unpatch(standable, HarmonyPatchType.All, host.Id); }
        }

        [Test]
        public void SuccessfulRemoteWorkTemporarilyFavorsSameGiverNearSite()
        {
            PrepareCombatFixture();
            pawn.needs = Bare<Pawn_NeedsTracker>();
            var giverDef = new WorkGiverDef();
            var giver = new WorkGiver_Repair { def = giverDef };
            Set(giverDef, "workerInt", giver);
            var remote = new IntVec3(70, 0, 5);
            var job = new Job { targetA = remote, workGiverDef = giverDef };
            RemoteWorkLocality.Started(pawn, job);
            Set(pawn, "positionInt", remote);
            RemoteWorkLocality.Finished(pawn, job, JobCondition.Succeeded);

            Assert.That(RemoteWorkLocality.TryActivate(pawn), Is.True);
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, new IntVec3(71, 0, 5)), Is.False);
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, Inside), Is.True);
            RemoteWorkLocality.Deactivate();
            Assert.That(RemoteWorkLocality.CandidateIsTooFar(giver, pawn, Inside), Is.False);
            Set(ticks, "ticksGameInt", 2601);
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
            PrepareCombatFixture();
            pawn.needs = Bare<Pawn_NeedsTracker>();
            pawn.workSettings = Bare<Pawn_WorkSettings>();
            Set(pawn.workSettings, "pawn", pawn);
            var urgentDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "urgent" } };
            var remoteDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "remote" } };
            var trivialDef = new WorkGiverDef { workType = new WorkTypeDef { defName = "trivial" } };
            var urgent = new WorkGiver_Repair { def = urgentDef };
            var remoteGiver = new WorkGiver_Repair { def = remoteDef };
            var trivial = new WorkGiver_Repair { def = trivialDef };
            Set(remoteDef, "workerInt", remoteGiver);
            var site = new IntVec3(70, 0, 5);
            var job = new Job { targetA = site, workGiverDef = remoteDef };
            RemoteWorkLocality.Started(pawn, job);
            Set(pawn, "positionInt", site);
            RemoteWorkLocality.Finished(pawn, job, JobCondition.Succeeded);
            var host = new Harmony("BetterRimAI.tests.locality-priority");
            var priority = AccessTools.Method(typeof(Pawn_WorkSettings), "GetPriority", new[] { typeof(WorkTypeDef) });
            host.Patch(priority, prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(HeadlessWorkPriority))));
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
    }
}
