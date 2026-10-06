using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI.RimWorldTests
{
    /// <summary>
    /// Integration checks against the installed game's real IL and Harmony. They prove the
    /// interception surface and the no-threat pass-through; they do not simulate a colony, so
    /// the job lifecycle scenarios still need the manual checklist in the README.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class ThreatSafetyIntegrationTests
    {
        private static readonly string[] ExpectedNonCallSitePatches =
        {
            "JobGiver_Work.TryIssueJobPackage",
            "Pawn_JobTracker.EndCurrentJob",
            "Pawn_JobTracker.StartJob",
            "Pawn_PathFollower.StartPath",
            "Pawn_WorkSettings.get_WorkGiversInOrderNormal",
        };

        private static string Describe(MethodBase m) => m.DeclaringType.Name + "." + m.Name;

        private static bool IsJobGiverWorkMember(MethodBase m)
        {
            for (System.Type t = m.DeclaringType; t != null; t = t.DeclaringType)
                if (t == typeof(JobGiver_Work)) return true;
            return false;
        }

        [Test]
        public void CallSiteDiscoveryFindsTheAutonomousScannerCallsOnly()
        {
            List<MethodBase> targets = WorkScanCallSitePatch.FindTargets();
            Assert.That(targets, Is.Not.Empty, "installed JobGiver_Work no longer calls HasJobOnThing/HasJobOnCell directly");
            Assert.That(targets.All(IsJobGiverWorkMember), Is.True);
            Assert.That(targets.Any(m => m.Name.Contains("GiverTryGiveJobPrioritized")), Is.False,
                "player Prioritize orders must stay vanilla");
            bool thing = false, cell = false;
            foreach (MethodBase m in targets)
                foreach (KeyValuePair<OpCode, object> i in PatchProcessor.ReadMethodBody(m))
                {
                    thing |= WorkScanCallSitePatch.IsCallTo(i.Key, i.Value, WorkScanCallSitePatch.HasJobOnThing);
                    cell |= WorkScanCallSitePatch.IsCallTo(i.Key, i.Value, WorkScanCallSitePatch.HasJobOnCell);
                }
            Assert.That(thing, Is.True, "thing candidate validator");
            Assert.That(cell, Is.True, "cell candidate processor");
            TestContext.WriteLine("Call-site targets: " + string.Join(", ", targets.Select(Describe)));
        }

        [Test]
        public void HarmonySurfaceIsSmallAndAvoidsHotOrUnrelatedMethods()
        {
            var harmony = new Harmony("BetterRimAI.tests.surface");
            try
            {
                harmony.PatchAll(typeof(ThreatSafetyPolicy).Assembly);
                List<MethodBase> patched = harmony.GetPatchedMethods()
                    .Where(m => Harmony.GetPatchInfo(m).Owners.Contains(harmony.Id)).ToList();
                TestContext.WriteLine("Patched: " + string.Join(", ", patched.Select(Describe)));

                List<string> other = patched.Where(m => !(IsJobGiverWorkMember(m) && m.Name != nameof(JobGiver_Work.TryIssueJobPackage)))
                    .Select(Describe).Distinct().OrderBy(n => n).ToList();
                Assert.That(other, Is.EqualTo(ExpectedNonCallSitePatches));
                int callSites = patched.Count(m => IsJobGiverWorkMember(m) && m.Name != nameof(JobGiver_Work.TryIssueJobPackage));
                Assert.That(callSites, Is.InRange(1, 4));

                string[] forbidden = { "TryEnterNextPathCell", "PatherTick", "JobTrackerTick", "JobTrackerTickInterval", "GetInspectString" };
                Assert.That(patched.Where(m => forbidden.Contains(m.Name)), Is.Empty);
                Assert.That(patched.Where(m => typeof(WorkGiver).IsAssignableFrom(m.DeclaringType)), Is.Empty,
                    "no per-WorkGiver patches");
                Assert.That(patched.Where(m => typeof(ThinkNode_JobGiver).IsAssignableFrom(m.DeclaringType)), Is.Empty,
                    "no universal ThinkNode filter");

                foreach (MethodBase site in WorkScanCallSitePatch.FindTargets())
                {
                    List<CodeInstruction> code = PatchProcessor.GetCurrentInstructions(site);
                    Assert.That(code.Any(c => WorkScanCallSitePatch.IsCallTo(c.opcode, c.operand, WorkScanCallSitePatch.HasJobOnThing)
                                              || WorkScanCallSitePatch.IsCallTo(c.opcode, c.operand, WorkScanCallSitePatch.HasJobOnCell)),
                        Is.False, Describe(site) + " still calls a scanner directly");
                    Assert.That(code.Any(c => c.operand is MethodInfo mi && mi.DeclaringType == typeof(WorkScanFilter)), Is.True);
                }
            }
            finally { harmony.UnpatchAll(harmony.Id); }
        }

        private static int scannerCalls;
        private static object lastThing;
        private static bool lastForced;
        private static bool scannerAnswer;

        private static bool RecordThing(Thing __1, bool __2, ref bool __result)
        {
            scannerCalls++;
            lastThing = __1;
            lastForced = __2;
            __result = scannerAnswer;
            return false;
        }

        [Test]
        public void NoThreat_SnapshotRefreshSucceedsWithoutFailingOpen()
        {
            // The snapshot fails open (inactive + one logged error) when its refresh throws. The
            // no-threat invariant must hold because nothing is hostile, not because of that.
            // Each vanilla dependency is checked separately so a failure names its cause.
            var colony = new HeadlessColony();
            try
            {
                Faction player = null;
                Assert.DoesNotThrow(() => player = Faction.OfPlayer, "vanilla Faction.OfPlayer in the fixture");
                Assert.That(player, Is.SameAs(colony.Player));
                HashSet<IAttackTarget> hostiles = null;
                Assert.DoesNotThrow(() => hostiles = colony.Map.attackTargetsCache.TargetsHostileToColony,
                    "vanilla AttackTargetsCache.TargetsHostileToColony in the fixture");
                Assert.That(hostiles, Is.Empty);
                LogCapture.AssertNoErrors("the vanilla hostile-target lookup");

                MapThreatState state = MapThreatState.For(colony.Map);
                Assert.That(state.Active, Is.False);
                Assert.That(state.ThreatCount, Is.EqualTo(0));
                Assert.That(state.Walkable, Is.Null, "no grids are built without a threat");
                LogCapture.AssertNoErrors("the no-threat snapshot refresh");
            }
            finally { colony.Dispose(); }
        }

        [Test]
        public void NoThreat_FilterIsAPlainVanillaCall_AndNoRestrictionOpens()
        {
            var colony = new HeadlessColony();
            var host = new Harmony("BetterRimAI.tests.passthrough");
            MethodInfo repair = AccessTools.DeclaredMethod(typeof(WorkGiver_Repair), nameof(WorkGiver_Scanner.HasJobOnThing));
            host.Patch(repair, prefix: new HarmonyMethod(AccessTools.Method(typeof(ThreatSafetyIntegrationTests), nameof(RecordThing))));
            try
            {
                Pawn pawn = colony.Pawn;
                Assert.That(MapThreatState.For(colony.Map).Active, Is.False, "empty hostile set");
                var giver = new WorkGiver_Repair { def = new WorkGiverDef() };
                Assert.That(ScanRestriction.TryBegin(new JobGiver_Work(), pawn), Is.Null,
                    "no restriction exists without an active threat");

                Thing wall = colony.MakeThing(40, HeadlessColony.Outside);
                foreach (bool answer in new[] { true, false })
                {
                    scannerCalls = 0;
                    scannerAnswer = answer;
                    Assert.That(WorkScanFilter.HasJobOnThing(giver, pawn, wall, false), Is.EqualTo(answer));
                    Assert.That(scannerCalls, Is.EqualTo(1));
                    Assert.That(lastThing, Is.SameAs(wall));
                    Assert.That(lastForced, Is.False);
                }
                LogCapture.AssertNoErrors("the no-threat pass-through");
            }
            finally
            {
                host.UnpatchAll(host.Id);
                colony.Dispose();
            }
        }

        [Test]
        public void NoThreat_StartPathIsNeverRewritten()
        {
            var colony = new HeadlessColony();
            try
            {
                Pawn pawn = colony.Pawn;
                pawn.jobs = HeadlessColony.Bare<Pawn_JobTracker>();
                pawn.jobs.curJob = new Job { def = new JobDef { defName = "BuildRoof" }, workGiverDef = new WorkGiverDef(),
                    targetA = new IntVec3(6, 0, 6), targetB = new IntVec3(6, 0, 6) };
                LocalTargetInfo dest = new IntVec3(6, 0, 6);
                PathEndMode mode = PathEndMode.Touch;
                SafeSidePathPatch.Prefix(pawn, ref dest, ref mode);
                Assert.That(dest.Cell, Is.EqualTo(new IntVec3(6, 0, 6)));
                Assert.That(mode, Is.EqualTo(PathEndMode.Touch));
                LogCapture.AssertNoErrors("the no-threat StartPath check");
            }
            finally { colony.Dispose(); }
        }

        [Test]
        public void PlayerOverridesBypassThreatSafety()
        {
            var colony = new HeadlessColony();
            try
            {
                Pawn pawn = colony.Pawn;
                Assert.That(ThreatSafetyPolicy.Eligible(pawn), Is.True);
                HeadlessColony.Set(pawn.drafter, "draftedInt", true);
                Assert.That(ThreatSafetyPolicy.Eligible(pawn), Is.False, "drafted");
                HeadlessColony.Set(pawn.drafter, "draftedInt", false);
                BetterRimAIMod.Settings.threatAwareOutdoorWork = false;
                Assert.That(ThreatSafetyPolicy.Eligible(pawn), Is.False, "feature disabled");
            }
            finally { colony.Dispose(); }
        }
    }
}
