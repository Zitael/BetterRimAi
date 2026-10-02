using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace BetterRimAI.Tests
{
    [TestFixture, NonParallelizable]
    public partial class CandidateSelectionTests
    {
        private Game previousGame;
        private BetterRimAISettings previousSettings;
        private Map map;
        private Pawn pawn;
        private Thing outdoor, indoor;
        private TickManager ticks;
        private BoolGrid homeGrid;
        private static readonly IntVec3 Outside = new IntVec3(1, 0, 1);
        private static readonly IntVec3 Inside = new IntVec3(5, 0, 5);

        private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);

        [SetUp]
        public void Setup()
        {
            previousGame = Current.Game;
            previousSettings = BetterRimAIMod.Settings;
            var game = Bare<Game>();
            ticks = Bare<TickManager>();
            Set(ticks, "ticksGameInt", 100);
            game.tickManager = ticks;
            map = Bare<Map>();
            map.uniqueID = 71;
            var info = Bare<MapInfo>();
            Set(info, "sizeInt", new IntVec3(10, 1, 10));
            Set(map, "info", info);
            map.cellIndices = new CellIndices(map);
            Set(game, "maps", new List<Map> { map });
            var world = Bare<World>();
            world.factionManager = Bare<FactionManager>();
            var faction = Bare<Faction>();
            faction.def = new FactionDef { isPlayer = true };
            Set(world.factionManager, "ofPlayer", faction);
            Set(game, "worldInt", world);
            SetCurrent(game);
            SetSettings(new BetterRimAISettings { threatAwareOutdoorWork = true, threatDebugLogging = false });
            new ThreatAwareGameState(game);
            map.areaManager = Bare<AreaManager>();
            Set(map.areaManager, "map", map);
            var home = Bare<Area_Home>();
            Set(home, "areaManager", map.areaManager);
            homeGrid = new BoolGrid(map);
            homeGrid[Inside] = true;
            Set(home, "innerGrid", homeGrid);
            Set(map.areaManager, "areas", new List<Area> { home });
            map.mapPawns = Bare<MapPawns>();
            Set(map.mapPawns, "pawnsSpawned", new List<Pawn>());
            pawn = Bare<Pawn>();
            pawn.thingIDNumber = 17;
            pawn.def = Bare<ThingDef>();
            pawn.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            Set(pawn, "factionInt", faction);
            Set(pawn, "mapIndexOrState", (sbyte)0);
            Set(pawn, "positionInt", Inside);
            pawn.drafter = Bare<Pawn_DraftController>();
            pawn.playerSettings = Bare<Pawn_PlayerSettings>();
            Set(pawn.playerSettings, "pawn", pawn);
            pawn.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
            outdoor = MakeThing(20, Outside);
            indoor = MakeThing(21, Inside);
            ThreatAwareOutdoorRetryCooldown.Remember(pawn, Outside, 15, 100);
        }

        private static void SetCurrent(Game game) => AccessTools.Field(typeof(Current), "gameInt").SetValue(null, game);
        private static void SetSettings(BetterRimAISettings settings) => BetterRimAIMod.Settings = settings;
        private Thing MakeThing(int id, IntVec3 cell)
        {
            var thing = Bare<Thing>();
            thing.thingIDNumber = id;
            Set(thing, "mapIndexOrState", (sbyte)0);
            Set(thing, "positionInt", cell);
            return thing;
        }

        [TearDown]
        public void TearDown()
        {
            ThreatAwareOutdoorWorkPatch.Reset();
            ThreatAwareOutdoorRetryCooldown.Reset();
            ThreatAwareHomeSafety.Reset();
            SetCurrent(previousGame);
            SetSettings(previousSettings);
        }

        [Test]
        public void AutonomousUnsafeThingAndCellAreRejectedBeforeMovement()
        {
            bool result = true;
            Assert.That(ThreatAwareBlockedThingCandidatePatch.Prefix(new RenamedScanner(), pawn, outdoor, false, ref result), Is.False);
            Assert.That(result, Is.False);
            result = true;
            Assert.That(ThreatAwareBlockedCellCandidatePatch.Prefix(new RenamedScanner(), pawn, Outside, false, ref result), Is.False);
            Assert.That(result, Is.False);
        }

        [Test]
        public void DangerousTargetDoesNotSuppressUnrelatedIndoorWorkOfSameJobDef()
        {
            var def = new JobDef { defName = "HaulToInventory" };
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { def = def, targetA = outdoor }), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { def = def, targetA = indoor }), Is.False);
            bool result = true;
            Assert.That(ThreatAwareBlockedThingCandidatePatch.Prefix(new RenamedScanner(), pawn, indoor, false, ref result), Is.True);
        }

        [Test]
        public void DraftedPawnCanLeave()
        {
            Set(pawn.drafter, "draftedInt", true);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void ExplicitForcedCandidateAndJobCanLeave()
        {
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, true), Is.False);
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { targetA = outdoor, playerForced = true }), Is.False);
        }

        [Test]
        public void AttackResponseCanLeave()
        {
            pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            Assert.That(pawn.playerSettings.UsesConfigurableHostilityResponse, Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void ThreatDisappearanceClearsAtExpiryAndDoesNotStrandPawn()
        {
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            Set(ticks, "ticksGameInt", 10000);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void ThinkNodeFallbackRejectsOutdoorAndPreservesIndoorNeedsJob()
        {
            var node = new JobGiver_GetRest();
            var result = new ThinkResult(new Job { targetA = outdoor }, node);
            ThreatAwareThinkNodePatch.Postfix(pawn, ref result);
            Assert.That(result.IsValid, Is.False);
            result = new ThinkResult(new Job { targetA = indoor }, node);
            ThreatAwareThinkNodePatch.Postfix(pawn, ref result);
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void IndoorPrimaryDoesNotHideOutdoorPuahQueueOrSecondaryTarget()
        {
            var job = new Job { targetA = indoor, targetQueueA = new List<LocalTargetInfo> { outdoor } };
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job), Is.True);
            job.targetQueueA.Clear();
            job.targetB = outdoor;
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, job), Is.True);
        }

        [Test]
        public void EnclosedUnpaintedPocketStillAllowsIndoorWork()
        {
            homeGrid[Inside] = false;
            homeGrid[new IntVec3(4, 0, 5)] = true;
            homeGrid[new IntVec3(6, 0, 5)] = true;
            homeGrid[new IntVec3(5, 0, 4)] = true;
            homeGrid[new IntVec3(5, 0, 6)] = true;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
        }

        [Test]
        public void DisabledFeatureAllowsAllCandidates()
        {
            BetterRimAIMod.Settings.threatAwareOutdoorWork = false;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }
        private void SeedCachedHostile(int tick)
        {
            // Simulate the cached hostile snapshot produced by a prior route check.
            var get = AccessTools.Method(typeof(ThreatAwareOutdoorWorkPatch), "GetRelevantHostilesCached");
            var hostiles = (List<ThreatAwareThreat>)get.Invoke(null, new object[] { pawn, map, tick });
            var hostile = Bare<Pawn>();
            Set(hostile, "mapIndexOrState", (sbyte)0);
            Set(hostile, "positionInt", Outside);
            hostile.health = Bare<Pawn_HealthTracker>();
            Set(hostile.health, "healthState", PawnHealthState.Mobile);
            hostiles.Add(new ThreatAwareThreat(hostile));
        }

        [Test]
        public void PersistentCachedDangerRenewsRestrictionWithoutRetryWindow()
        {
            Set(ticks, "ticksGameInt", 220);
            SeedCachedHostile(220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Set(ticks, "ticksGameInt", 340);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void KnownTargetIsIndexedAndClearsAfterThreatDisappearance()
        {
            ThreatAwareOutdoorRetryCooldown.Reset();
            var job = new Job { def = new JobDef { defName = "HaulToInventory" }, targetA = outdoor };
            AccessTools.Method(typeof(ThreatAwareOutdoorWorkPatch), "RememberGlobalBlock")
                .Invoke(null, new object[] { map, job, Outside, Outside, 15f });
            SeedCachedHostile(100);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void WarmCandidateLoopDoesNotTouchHostileRosterOrPathfinder()
        {
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            map.mapPawns = null;
            // The uninitialized Map also has no pathfinder.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int rejected = 0;
            for (int i = 0; i < 100000; i++)
                if (ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false)) rejected++;
            Assert.That(rejected, Is.EqualTo(100000));
            TestContext.WriteLine("100,000 warmed candidate checks: " + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms (not an in-game FPS measurement)");
        }

        [Test]
        public void NewGameClearsEvidenceEvenWhenPawnIdsAreReused()
        {
            new ThreatAwareGameState(Current.Game);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [TestCase("drafted")]
        [TestCase("forced")]
        [TestCase("attack")]
        [TestCase("disabled")]
        public void DeferredCancellationHonorsOverrideThatArrivesAfterPathStopped(string mode)
        {
            pawn.jobs = Bare<Pawn_JobTracker>();
            var job = new Job { targetA = outdoor };
            pawn.jobs.curJob = job;
            ThreatAwarePendingCancellation.Schedule(pawn, job);
            if (mode == "drafted") Set(pawn.drafter, "draftedInt", true);
            if (mode == "forced") job.playerForced = true;
            if (mode == "attack") pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            if (mode == "disabled") BetterRimAIMod.Settings.threatAwareOutdoorWork = false;
            ThreatAwarePendingCancellation.Prefix(pawn);
            Assert.That(pawn.CurJob, Is.SameAs(job));
        }
        public class RenamedScanner : WorkGiver_Scanner
        {
            public override PathEndMode PathEndMode => PathEndMode.OnCell;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public override bool HasJobOnThing(Pawn worker, Thing item, bool manual = false) => true;
        }

        public class RenamedCellScanner : WorkGiver_Scanner
        {
            public override PathEndMode PathEndMode => PathEndMode.OnCell;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public override bool HasJobOnCell(Pawn worker, IntVec3 tile, bool manual = false) => true;
        }

        [Test]
        public void CellScannerHarmonyPrefixBindsInstanceAndForeignParameterNames()
        {
            var harmony = new Harmony("BetterRimAI.tests.cell-positional");
            MethodInfo original = AccessTools.DeclaredMethod(typeof(RenamedCellScanner), "HasJobOnCell");
            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(typeof(ThreatAwareBlockedCellCandidatePatch), "Prefix")));
                var scanner = new RenamedCellScanner();
                Assert.That((bool)original.Invoke(scanner, new object[] { pawn, Outside, false }), Is.False);
                Assert.That((bool)original.Invoke(scanner, new object[] { pawn, Inside, false }), Is.True);
                Assert.That((bool)original.Invoke(scanner, new object[] { pawn, Outside, true }), Is.True);
            }
            finally { harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id); }
        }

        [TestCase(typeof(ThreatAwareBlockedThingCandidatePatch))]
        [TestCase(typeof(PickUpAndHaulBlockedCandidatePatch))]
        public void HarmonyPrefixBindsForeignNamesAndKeepsSearchingIndoorCandidates(Type patchType)
        {
            var harmony = new Harmony("BetterRimAI.tests.positional");
            MethodInfo original = AccessTools.DeclaredMethod(typeof(RenamedScanner), "HasJobOnThing");
            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.Method(patchType, "Prefix")));
                var scanner = Bare<RenamedScanner>();
                Thing selected = null;
                foreach (Thing target in new[] { outdoor, indoor })
                {
                    if (!(bool)original.Invoke(scanner, new object[] { pawn, target, false })) continue;
                    selected = target;
                    break;
                }
                Assert.That(selected, Is.SameAs(indoor));
                Assert.That((bool)original.Invoke(scanner, new object[] { pawn, outdoor, true }), Is.True);
            }
            finally { harmony.Unpatch(original, HarmonyPatchType.All, harmony.Id); }
        }
        [Test]
        public void StalePathBlockCannotCancelAgainAfterThreatDisappears()
        {
            pawn.jobs = Bare<Pawn_JobTracker>();
            pawn.jobs.curJob = new Job { targetA = outdoor };
            var pather = Bare<Pawn_PathFollower>();
            Set(pather, "destination", new LocalTargetInfo(outdoor));
            Type stateType = typeof(ThreatAwareOutdoorWorkPatch).GetNestedType("PathCheckState", BindingFlags.NonPublic);
            object state = Activator.CreateInstance(stateType, true);
            Set(state, "blocked", true);
            Set(state, "blockedThingId", outdoor.thingIDNumber);
            var states = (System.Collections.IDictionary)AccessTools.Field(typeof(ThreatAwareOutdoorWorkPatch), "CheckStateByPawn").GetValue(null);
            states.Add(pawn.thingIDNumber, state);
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.Prefix(pather, pawn), Is.True);
        }

        [Test]
        public void HeadlessMissingTerrainDoesNotBlockOutdoorCandidates()
        {
            ThreatAwareOutdoorRetryCooldown.Reset();
            map.mapPawns = null;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void PawnTransferToAnotherMapDoesNotCarryOldRestriction()
        {
            var otherMap = Bare<Map>();
            otherMap.uniqueID = 72;
            Current.Game.Maps.Add(otherMap);
            Set(pawn, "mapIndexOrState", (sbyte)1);
            Assert.That(ThreatAwareOutdoorRetryCooldown.IsRestricted(pawn), Is.False);
        }

        [Test]
        public void MovedIndoorTargetIsAllowedEvenWhenPreviouslyBlocked()
        {
            Set(outdoor, "positionInt", Inside);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }
    }
}
