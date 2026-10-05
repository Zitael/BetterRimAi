using System;
using System.Collections.Generic;
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
        private Harmony host;
        private static readonly HashSet<IntVec3> walls = new HashSet<IntVec3>();
        private Map map;
        private Pawn pawn;
        private Thing outdoor, indoor;
        private TickManager ticks;
        private BoolGrid homeGrid;
        private static readonly IntVec3 Outside = new IntVec3(1, 0, 1);
        private static readonly IntVec3 Inside = new IntVec3(5, 0, 5);

        private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        private static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        private static void SetCurrent(Game game) => AccessTools.Field(typeof(Current), "gameInt").SetValue(null, game);
        private static bool Standable(IntVec3 __0, ref bool __result) { __result = !walls.Contains(__0); return false; }
        private static bool False(ref bool __result) { __result = false; return false; }
        private static bool EmptyVanillaInspect(ref string __result) { __result = string.Empty; return false; }
        private static bool ActiveThreat(IAttackTarget __0, ref bool __result)
        {
            Pawn p = __0?.Thing as Pawn;
            __result = p != null && p.Spawned && !p.Dead && !p.Downed;
            return false;
        }

        [SetUp]
        public void Setup()
        {
            previousGame = Current.Game;
            previousSettings = BetterRimAIMod.Settings;
            walls.Clear();
            var game = Bare<Game>();
            ticks = Bare<TickManager>();
            Set(ticks, "ticksGameInt", 100);
            game.tickManager = ticks;
            map = Bare<Map>();
            map.uniqueID = 71;
            var info = Bare<MapInfo>();
            Set(info, "sizeInt", new IntVec3(80, 1, 80));
            Set(map, "info", info);
            map.cellIndices = new CellIndices(map);
            map.terrainGrid = Bare<TerrainGrid>();
            Set(game, "maps", new List<Map> { map });
            var world = Bare<World>();
            world.factionManager = Bare<FactionManager>();
            var faction = Bare<Faction>();
            faction.def = new FactionDef { isPlayer = true };
            Set(world.factionManager, "ofPlayer", faction);
            Set(game, "worldInt", world);
            SetCurrent(game);
            BetterRimAIMod.Settings = new BetterRimAISettings { threatAwareOutdoorWork = true, threatDebugLogging = false };
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
            map.attackTargetsCache = Bare<AttackTargetsCache>();
            Set(map.attackTargetsCache, "map", map);
            Set(map.attackTargetsCache, "allTargets", new HashSet<IAttackTarget>());
            Set(map.attackTargetsCache, "targetsHostileToFaction", new Dictionary<Faction, HashSet<IAttackTarget>>
            {
                [faction] = new HashSet<IAttackTarget>()
            });
            Set(map.attackTargetsCache, "pawnsInAggroMentalState", new HashSet<Pawn>());
            Set(map.attackTargetsCache, "factionlessHumanlikes", new HashSet<Pawn>());
            pawn = Bare<Pawn>();
            pawn.thingIDNumber = 17;
            pawn.def = Bare<ThingDef>();
            pawn.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            pawn.kindDef = new PawnKindDef();
            pawn.health = Bare<Pawn_HealthTracker>();
            Set(pawn.health, "healthState", PawnHealthState.Mobile);
            pawn.mindState = Bare<Pawn_MindState>();
            pawn.mindState.mentalStateHandler = Bare<MentalStateHandler>();
            Set(pawn.mindState, "pawn", pawn);
            Set(pawn.mindState.mentalStateHandler, "pawn", pawn);
            Set(pawn, "factionInt", faction);
            Set(pawn, "mapIndexOrState", (sbyte)0);
            Set(pawn, "positionInt", Inside);
            pawn.drafter = Bare<Pawn_DraftController>();
            pawn.playerSettings = Bare<Pawn_PlayerSettings>();
            Set(pawn.playerSettings, "pawn", pawn);
            pawn.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
            outdoor = MakeThing(20, Outside);
            indoor = MakeThing(21, Inside);
            host = new Harmony("BetterRimAI.tests.v2-geometry");
            host.Patch(AccessTools.Method(typeof(GenGrid), "Standable", new[] { typeof(IntVec3), typeof(Map) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(Standable))));
            host.Patch(AccessTools.Method(typeof(MechanitorUtility), "IsPlayerOverseerSubject"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(False))));
            host.Patch(AccessTools.PropertyGetter(typeof(Pawn), "IsShambler"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(False))));
            host.Patch(AccessTools.Method(typeof(InvisibilityUtility), "IsPsychologicallyInvisible"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(False))));
            host.Patch(AccessTools.Method(typeof(ForbidUtility), "IsForbidden", new[] { typeof(IntVec3), typeof(Pawn) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(False))));
            host.Patch(AccessTools.Method(typeof(GenHostility), "IsPotentialThreat", new[] { typeof(IAttackTarget) }),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(ActiveThreat))));
        }

        [TearDown]
        public void TearDown()
        {
            host?.UnpatchAll(host.Id);
            new ThreatAwareGameState(Current.Game);
            SetCurrent(previousGame);
            BetterRimAIMod.Settings = previousSettings;
        }

        private Thing MakeThing(int id, IntVec3 cell)
        {
            var thing = Bare<Thing>();
            thing.thingIDNumber = id;
            thing.def = Bare<ThingDef>();
            thing.def.size = new IntVec2(1, 1);
            Set(thing, "mapIndexOrState", (sbyte)0);
            Set(thing, "positionInt", cell);
            return thing;
        }

        private void PrepareCombatFixture() => AddHostile(Outside);
        private Pawn AddHostile(IntVec3 cell)
        {
            var hostile = Bare<Pawn>();
            hostile.thingIDNumber = 41;
            hostile.def = Bare<ThingDef>();
            hostile.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            hostile.kindDef = new PawnKindDef { defName = "raider" };
            hostile.health = Bare<Pawn_HealthTracker>();
            Set(hostile.health, "healthState", PawnHealthState.Mobile);
            Set(hostile, "positionInt", cell);
            Set(hostile, "mapIndexOrState", (sbyte)0);
            hostile.mindState = Bare<Pawn_MindState>();
            hostile.mindState.mentalStateHandler = Bare<MentalStateHandler>();
            Set(hostile.mindState, "pawn", hostile);
            Set(hostile.mindState.mentalStateHandler, "pawn", hostile);
            var enemy = Bare<Faction>();
            enemy.def = new FactionDef { defName = "Enemy" };
            Set(enemy, "relations", new List<FactionRelation> { new FactionRelation { other = Faction.OfPlayer, kind = FactionRelationKind.Hostile } });
            Set(Faction.OfPlayer, "relations", new List<FactionRelation> { new FactionRelation { other = enemy, kind = FactionRelationKind.Hostile } });
            Set(enemy, "predatorThreats", Activator.CreateInstance(AccessTools.Field(typeof(Faction), "predatorThreats").FieldType));
            Set(Faction.OfPlayer, "predatorThreats", Activator.CreateInstance(AccessTools.Field(typeof(Faction), "predatorThreats").FieldType));
            Set(hostile, "factionInt", enemy);
            ((List<Pawn>)AccessTools.Field(typeof(MapPawns), "pawnsSpawned").GetValue(map.mapPawns)).Add(hostile);
            return hostile;
        }

        private sealed class TestTurret : Building_Turret
        {
            internal Verb verb;
            public override Verb AttackVerb => verb;
            public override LocalTargetInfo CurrentTarget => LocalTargetInfo.Invalid;
            public override void OrderAttack(LocalTargetInfo target) { }
        }
        private sealed class CountingShot : Verb
        {
            internal bool canHit;
            internal int calls;
            public override float EffectiveRange => verbProps.range;
            protected override bool TryCastShot() => true;
            public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target)
            {
                calls++;
                return canHit && target.Cell == Outside;
            }
        }
        private sealed class HeadlessProjectile : Verb_Shoot
        {
            public override float EffectiveRange => verbProps.range;
        }
        private CountingShot AddTurret()
        {
            Pawn factionCarrier = AddHostile(new IntVec3(70, 0, 70));
            Set(factionCarrier, "mapIndexOrState", (sbyte)-1);
            var turret = new TestTurret();
            turret.def = Bare<ThingDef>();
            turret.def.size = new IntVec2(1, 1);
            Set(turret, "positionInt", new IntVec3(2, 0, 1));
            Set(turret, "mapIndexOrState", (sbyte)0);
            Set(turret, "factionInt", factionCarrier.Faction);
            Set(turret, "comps", new List<ThingComp> { new DummyComp() });
            var shot = new CountingShot { canHit = true };
            shot.verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), range = 35f,
                defaultProjectile = Bare<ThingDef>() };
            shot.caster = turret;
            shot.verbTracker = new VerbTracker(new CompEquippable { parent = new ThingWithComps { def = Bare<ThingDef>() } });
            turret.verb = shot;
            ((Dictionary<Faction, HashSet<IAttackTarget>>)AccessTools.Field(typeof(AttackTargetsCache), "targetsHostileToFaction")
                .GetValue(map.attackTargetsCache))[Faction.OfPlayer].Add(turret);
            return shot;
        }
        private sealed class DummyComp : ThingComp { }

        [Test]
        public void RaidRejectsOutdoorCandidateButAllowsIndoorWork()
        {
            AddHostile(Outside);
            bool result = true;
            Assert.That(ThreatAwareBlockedThingCandidatePatch.Prefix(new RenamedScanner(), pawn, outdoor, false, ref result), Is.False);
            Assert.That(result, Is.False);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
        }

        [Test]
        public void SafeOutdoorWorkAndOverridesRemainAvailable()
        {
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            AddHostile(Outside);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, true), Is.False);
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { targetA = outdoor, playerForced = true }), Is.False);
            Set(pawn.drafter, "draftedInt", true);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            Set(pawn.drafter, "draftedInt", false);
            pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void SealedHostileDoesNotProjectDangerThroughWallAndOpeningRechecks()
        {
            var center = new IntVec3(10, 0, 10);
            for (int x = 9; x <= 11; x++) { walls.Add(new IntVec3(x, 0, 9)); walls.Add(new IntVec3(x, 0, 11)); }
            for (int z = 9; z <= 11; z++) { walls.Add(new IntVec3(9, 0, z)); walls.Add(new IntVec3(11, 0, z)); }
            AddHostile(center);
            var target = MakeThing(30, new IntVec3(12, 0, 10));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, target, false), Is.False);
            walls.Remove(new IntVec3(11, 0, 10));
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, target, false), Is.True);
        }

        [Test]
        public void OutsidePawnCanReturnAndIsNeverRestrictedByExitPolicy()
        {
            AddHostile(Outside);
            Set(pawn, "positionInt", new IntVec3(2, 0, 1));
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { targetA = indoor }), Is.False);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            var pather = Bare<Pawn_PathFollower>();
            Assert.That(ThreatAwareOutdoorWorkPatch.Prefix(pather, pawn), Is.True);
            Assert.That(ThreatAwareDecision.Inspect(pawn), Does.Contain("return allowed"));
        }

        [Test]
        public void EnclosedUnpaintedHomePocketRemainsProtected()
        {
            AddHostile(Outside);
            homeGrid[Inside] = false;
            homeGrid[new IntVec3(4, 0, 5)] = true;
            homeGrid[new IntVec3(6, 0, 5)] = true;
            homeGrid[new IntVec3(5, 0, 4)] = true;
            homeGrid[new IntVec3(5, 0, 6)] = true;
            ThreatAwareHomeSafety.Reset();
            Assert.That(ThreatAwareHomeSafety.IsSafeCell(map, map.areaManager.Home, Inside), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
        }

        [Test]
        public void DisappearingThreatClearsWithoutCooldownOrPermanentTargetBan()
        {
            Pawn hostile = AddHostile(Outside);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Set(hostile, "mapIndexOrState", (sbyte)-1);
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void ObservedUnsafeVanillaRouteDoesNotRetryForeverOrBanIndoorWork()
        {
            Pawn hostile = AddHostile(new IntVec3(30, 0, 5));
            var remote = MakeThing(31, new IntVec3(60, 0, 60));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, remote, false), Is.False,
                "an alternate safe route exists in the map flood");
            ThreatAwareOutdoorSafetyMap.ObserveUnsafeRoute(pawn, remote, new IntVec3(30, 0, 5));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, remote, false), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
            Set(hostile, "mapIndexOrState", (sbyte)-1);
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, remote, false), Is.False);
        }

        [Test]
        public void TurretRequiresActualFiringLaneAndRechecksWhenOpened()
        {
            CountingShot shot = AddTurret();
            shot.canHit = false;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            int calls = shot.calls;
            for (int i = 0; i < 1000; i++)
                Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            Assert.That(shot.calls, Is.EqualTo(calls), "warm candidates must not invoke turret geometry");
            shot.canHit = true;
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
        }

        [Test]
        public void RealTurretVerbRespectsWallObstructionAfterSnapshotExpiry()
        {
            CountingShot fake = AddTurret();
            var turret = (TestTurret)fake.caster;
            Set(turret, "positionInt", new IntVec3(10, 0, 1));
            map.edificeGrid = Bare<EdificeGrid>();
            Set(map.edificeGrid, "map", map);
            var buildings = new Building[map.Size.x * map.Size.z];
            Set(map.edificeGrid, "innerArray", buildings);
            var wall = new Building { def = Bare<ThingDef>() };
            wall.def.fillPercent = 1f;
            int wallIndex = map.cellIndices.CellToIndex(new IntVec3(6, 0, 1));
            for (int z = 0; z < map.Size.z; z++)
            {
                var cell = new IntVec3(6, 0, z);
                buildings[map.cellIndices.CellToIndex(cell)] = wall;
                walls.Add(cell);
            }
            var real = new HeadlessProjectile();
            real.verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), range = 35f,
                requireLineOfSight = true, defaultProjectile = Bare<ThingDef>() };
            real.caster = turret;
            real.verbTracker = fake.verbTracker;
            turret.verb = real;
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            buildings[wallIndex] = null;
            walls.Remove(new IntVec3(6, 0, 1));
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
        }

        [Test]
        public void DangerousOutdoorTargetDoesNotBanSameJobDefIndoorsOrPuahQueue()
        {
            AddHostile(Outside);
            var def = new JobDef { defName = "HaulToInventory" };
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { def = def, targetA = outdoor }), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, new Job { def = def, targetA = indoor }), Is.False);
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn,
                new Job { def = def, targetA = indoor, targetQueueA = new List<LocalTargetInfo> { outdoor } }), Is.True);
        }

        [Test]
        public void ThinkNodeFallbackRejectsUnsafeNeedsJobButKeepsIndoorAlternative()
        {
            AddHostile(Outside);
            var node = new JobGiver_GetRest();
            var result = new ThinkResult(new Job { targetA = outdoor }, node);
            ThreatAwareThinkNodePatch.Postfix(pawn, ref result);
            Assert.That(result.IsValid, Is.False);
            result = new ThinkResult(new Job { targetA = indoor }, node);
            ThreatAwareThinkNodePatch.Postfix(pawn, ref result);
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void WarmCandidatesShareOneSnapshotWithoutRosterAccess()
        {
            AddHostile(Outside);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            map.mapPawns = null;
            int rejected = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                if (ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false)) rejected++;
            watch.Stop();
            Assert.That(rejected, Is.EqualTo(10000));
            TestContext.WriteLine("10,000 warm outdoor candidates: " + watch.Elapsed.TotalMilliseconds.ToString("F1")
                + " ms (headless, not in-game FPS)");
        }

        [Test]
        public void ActualInspectInterfaceAlwaysShowsStatusAndRecentDecision()
        {
            var method = AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.GetInspectString));
            host.Patch(method,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(EmptyVanillaInspect))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ThreatAwareInspectPatch), nameof(ThreatAwareInspectPatch.Postfix))));
            Assert.That(((ISelectable)pawn).GetInspectString(), Does.Contain("BetterRimAI: No safety restriction"));
            ThreatAwareDecision.Remember(pawn, outdoor, null, "observed unsafe route",
                new Job { def = new JobDef { defName = "Repair" } });
            string blocked = ((ISelectable)pawn).GetInspectString();
            Assert.That(blocked, Does.Contain("BetterRimAI: BLOCKED"));
            Assert.That(blocked, Does.Contain("Job: Repair"));
            Set(ticks, "ticksGameInt", 3801);
            Assert.That(((ISelectable)pawn).GetInspectString(), Does.Contain("No safety restriction"));
        }

        private sealed class TouchThingScanner : WorkGiver_Scanner
        {
            public override PathEndMode PathEndMode => PathEndMode.Touch;
            public override bool HasJobOnThing(Pawn worker, Thing item, bool manual = false) => true;
        }

        [Test]
        public void ObservedTouchApproachWithDifferentPathCellIsRejectedOnNextVanillaScan()
        {
            PrepareCombatFixture();
            Thing boundaryWall = MakeThing(51, new IntVec3(6, 0, 5));
            var scanner = new TouchThingScanner();
            bool hasJob = true;
            Assert.That(ThreatAwareBlockedThingCandidatePatch.Prefix(scanner, pawn, boundaryWall, false, ref hasJob), Is.True,
                "safe-side Touch work is initially eligible");
            Job firstJob = new Job { def = new JobDef { defName = "Repair" }, targetA = boundaryWall };
            Assert.That(ThreatAwareOutdoorSafetyMap.ThreatensCell(pawn, Outside, out _), Is.True,
                "the boundary guard builds the shared threat snapshot before recording evidence");
            ThreatAwareOutdoorSafetyMap.ObserveUnsafeJob(pawn, firstJob,
                new IntVec3(7, 0, 5), Outside);

            // Model the next vanilla WorkGiver scan after the path guard cancelled firstJob.
            // The path's interaction cell differs from the Thing target, as in the real bug.
            hasJob = true;
            bool runOriginal = ThreatAwareBlockedThingCandidatePatch.Prefix(scanner, pawn, boundaryWall, false, ref hasJob);
            Assert.That(runOriginal, Is.False);
            Assert.That(hasJob, Is.False);
            Job secondJob = new Job { def = firstJob.def, targetA = boundaryWall };
            Assert.That(ThreatAwareOutdoorWorkPatch.ShouldSuppressWorkJob(pawn, secondJob, true), Is.True);
            hasJob = true;
            Assert.That(ThreatAwareBlockedThingCandidatePatch.Prefix(scanner, pawn, indoor, false, ref hasJob), Is.True);
            Assert.That(hasJob, Is.True, "vanilla may continue to a safe indoor candidate");
        }

        [Test]
        public void ObservedRouteMustOverrideSafeTouchExemption()
        {
            PrepareCombatFixture();
            Thing boundaryWall = MakeThing(53, new IntVec3(6, 0, 5));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, boundaryWall, false, true), Is.False);
            Assert.That(ThreatAwareOutdoorSafetyMap.ThreatensCell(pawn, Outside, out _), Is.True);
            ThreatAwareOutdoorSafetyMap.ObserveUnsafeRoute(pawn, boundaryWall, Outside);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, boundaryWall, false, true), Is.True,
                "v2 returned false here because safeTouch preceded observed-route evidence");
            Assert.That(ThreatAwareOutdoorPolicy.Reject(pawn, boundaryWall, false, true, out string reason), Is.True);
            Assert.That(reason, Is.EqualTo("observed unsafe route"));
        }

        [Test]
        public void ObservedExteriorJobTargetSurvivesPathDestinationMismatchUntilThreatClears()
        {
            Pawn hostile = AddHostile(new IntVec3(30, 0, 5));
            Thing target = MakeThing(52, new IntVec3(60, 0, 60));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, target, false), Is.False);
            var job = new Job { targetA = target };
            ThreatAwareOutdoorSafetyMap.ObserveUnsafeJob(pawn, job,
                new IntVec3(31, 0, 5), new IntVec3(30, 0, 5));
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, target, false), Is.True);
            Set(hostile, "mapIndexOrState", (sbyte)-1);
            Set(ticks, "ticksGameInt", 281);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, target, false), Is.False);
        }

        public class RenamedScanner : WorkGiver_Scanner
        {
            public override PathEndMode PathEndMode => PathEndMode.OnCell;
            public override bool HasJobOnThing(Pawn worker, Thing item, bool manual = false) => true;
        }
    }
}
