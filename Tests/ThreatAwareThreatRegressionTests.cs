using System;
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
        private Harmony headlessCombatHost;

        [OneTimeSetUp]
        public void SetUpHeadlessCombatHost()
        {
            // None of these fixtures is an overseer subject. The native DLC-settings loader
            // needs Unity; supply this single non-mech host fact while retaining HostileTo.
            headlessCombatHost = new Harmony("BetterRimAI.tests.non-mech-host");
            headlessCombatHost.Patch(AccessTools.Method(typeof(MechanitorUtility), "IsPlayerOverseerSubject"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(NotAnOverseerSubject))));
            headlessCombatHost.Patch(AccessTools.PropertyGetter(typeof(Pawn), "IsShambler"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(HeadlessShamblerKind))));
            headlessCombatHost.Patch(AccessTools.Method(typeof(InvisibilityUtility), "IsPsychologicallyInvisible"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CandidateSelectionTests), nameof(NotAnOverseerSubject))));
        }

        [OneTimeTearDown]
        public void TearDownHeadlessCombatHost()
        {
            headlessCombatHost.Unpatch(AccessTools.Method(typeof(MechanitorUtility), "IsPlayerOverseerSubject"), HarmonyPatchType.All, headlessCombatHost.Id);
            headlessCombatHost.Unpatch(AccessTools.PropertyGetter(typeof(Pawn), "IsShambler"), HarmonyPatchType.All, headlessCombatHost.Id);
            headlessCombatHost.Unpatch(AccessTools.Method(typeof(InvisibilityUtility), "IsPsychologicallyInvisible"), HarmonyPatchType.All, headlessCombatHost.Id);
        }

        private static bool NotAnOverseerSubject(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static bool HeadlessShamblerKind(Pawn __instance, ref bool __result)
        {
            __result = __instance.kindDef?.defName == "shambler";
            return false;
        }

        private void PrepareCombatFixture(int size = 80)
        {
            Set(AccessTools.Field(typeof(Map), "info").GetValue(map), "sizeInt", new IntVec3(size, 1, size));
            map.cellIndices = new CellIndices(map);
            homeGrid = new BoolGrid(map);
            homeGrid[Inside] = true;
            Set(map.areaManager.Home, "innerGrid", homeGrid);
            ThreatAwareHomeSafety.Reset();
            pawn.kindDef = new PawnKindDef();
            pawn.health = Bare<Pawn_HealthTracker>();
            Set(pawn.health, "healthState", PawnHealthState.Mobile);
            SetupMind(pawn);
            Set(Faction.OfPlayer, "relations", new List<FactionRelation>());
            Set(Faction.OfPlayer, "predatorThreats", Activator.CreateInstance(AccessTools.Field(typeof(Faction), "predatorThreats").FieldType));
            map.attackTargetsCache = Bare<AttackTargetsCache>();
            Set(map.attackTargetsCache, "map", map);
            Set(map.attackTargetsCache, "allTargets", new HashSet<IAttackTarget>());
            Set(map.attackTargetsCache, "targetsHostileToFaction", new Dictionary<Faction, HashSet<IAttackTarget>>
            {
                [Faction.OfPlayer] = new HashSet<IAttackTarget>()
            });
            Set(map.attackTargetsCache, "pawnsInAggroMentalState", new HashSet<Pawn>());
            Set(map.attackTargetsCache, "factionlessHumanlikes", new HashSet<Pawn>());
        }

        private static void SetupMind(Pawn p)
        {
            p.mindState = Bare<Pawn_MindState>();
            p.mindState.mentalStateHandler = Bare<MentalStateHandler>();
            Set(p.mindState, "pawn", p);
            Set(p.mindState.mentalStateHandler, "pawn", p);
        }

        private Faction MakeEnemyFaction()
        {
            var enemy = Bare<Faction>();
            enemy.def = new FactionDef { defName = "RegressionEnemy" };
            Set(enemy, "predatorThreats", Activator.CreateInstance(AccessTools.Field(typeof(Faction), "predatorThreats").FieldType));
            Set(enemy, "relations", new List<FactionRelation> { new FactionRelation { other = Faction.OfPlayer, kind = FactionRelationKind.Hostile } });
            ((List<FactionRelation>)AccessTools.Field(typeof(Faction), "relations").GetValue(Faction.OfPlayer))
                .Add(new FactionRelation { other = enemy, kind = FactionRelationKind.Hostile });
            return enemy;
        }

        private Pawn MakeCombatPawn(string kind)
        {
            var hostile = Bare<Pawn>();
            hostile.thingIDNumber = 41;
            hostile.def = Bare<ThingDef>();
            hostile.def.race = new RaceProperties { intelligence = kind == "insect" || kind == "manhunter" || kind == "animal" ? Intelligence.Animal : Intelligence.Humanlike };
            hostile.kindDef = new PawnKindDef { defName = kind, hostileToAll = kind == "shambler" };
            hostile.health = Bare<Pawn_HealthTracker>();
            Set(hostile.health, "healthState", PawnHealthState.Mobile);
            Set(hostile, "positionInt", Outside);
            Set(hostile, "mapIndexOrState", (sbyte)0);
            SetupMind(hostile);
            if (kind != "manhunter" && kind != "animal") Set(hostile, "factionInt", MakeEnemyFaction());
            if (kind == "manhunter")
            {
                var state = Bare<HeadlessManhunter>();
                state.pawn = hostile;
                Set(hostile.mindState.mentalStateHandler, "curStateInt", state);
            }
            ((List<Pawn>)AccessTools.Field(typeof(MapPawns), "pawnsSpawned").GetValue(map.mapPawns)).Add(hostile);
            return hostile;
        }

        private List<ThreatAwareThreat> ReadThreats(int tick)
        {
            return (List<ThreatAwareThreat>)AccessTools.Method(typeof(ThreatAwareOutdoorWorkPatch), "GetRelevantHostilesCached")
                .Invoke(null, new object[] { pawn, map, tick });
        }

        // Keep native ForceHostileTo(Thing) and mental-state dispatch. Only its DLC-dependent
        // faction branch is supplied by the headless host (ModsConfig requires Unity file paths).
        private class HeadlessManhunter : MentalState_Manhunter
        {
            public override bool ForceHostileTo(Faction faction) => true;
        }

        private class DummyTurretComp : ThingComp { }

        private class TestTurret : Building_Turret
        {
            internal Verb verb;
            public override Verb AttackVerb => verb;
            public override LocalTargetInfo CurrentTarget => LocalTargetInfo.Invalid;
            public override void OrderAttack(LocalTargetInfo target) { }
        }

        private class CountingShot : Verb
        {
            public override float EffectiveRange => verbProps.range;
            internal int hits;
            internal bool canHit = true;
            internal IntVec3 onlyCell = IntVec3.Invalid;
            protected override bool TryCastShot() => true;
            public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target)
            {
                hits++;
                return canHit && (!onlyCell.IsValid || target.Cell == onlyCell) && (root - target.Cell).LengthHorizontalSquared <= EffectiveRange * EffectiveRange;
            }
        }

        // Isolate weather hosting; retain native projectile availability and shooting geometry.
        private class HeadlessProjectile : Verb_Shoot
        {
            public override float EffectiveRange => verbProps.range;
        }

        private TestTurret MakeTurret(IntVec3 position, bool friendly = false, bool realVerb = false)
        {
            var turret = new TestTurret();
            turret.thingIDNumber = 42;
            turret.def = Bare<ThingDef>();
            turret.def.size = new IntVec2(1, 1);
            Set(turret, "positionInt", position);
            Set(turret, "mapIndexOrState", (sbyte)0);
            Set(turret, "factionInt", friendly ? Faction.OfPlayer : MakeEnemyFaction());
            Set(turret, "comps", new List<ThingComp> { new DummyTurretComp() });
            turret.verb = realVerb ? (Verb)new HeadlessProjectile() : new CountingShot();
            turret.verb.verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), range = 35f, minRange = 0f, requireLineOfSight = false, defaultProjectile = Bare<ThingDef>() };
            turret.verb.caster = turret;
            var gun = new ThingWithComps { def = Bare<ThingDef>() };
            turret.verb.verbTracker = new VerbTracker(new CompEquippable { parent = gun });
            var byFaction = (Dictionary<Faction, HashSet<IAttackTarget>>)AccessTools.Field(typeof(AttackTargetsCache), "targetsHostileToFaction").GetValue(map.attackTargetsCache);
            byFaction[Faction.OfPlayer].Add(turret);
            return turret;
        }

        [TestCase("pawn")]
        [TestCase("insect")]
        [TestCase("shambler")]
        [TestCase("manhunter")]
        public void AllHostileMobileKindsEnterThreatCacheAndBlockOutdoorRetries(string kind)
        {
            PrepareCombatFixture();
            Pawn hostile = MakeCombatPawn(kind);
            Assert.That(hostile.HostileTo(pawn), Is.True, "vanilla hostility for " + kind);
            List<ThreatAwareThreat> threats = ReadThreats(100);
            Assert.That(threats.Exists(t => ReferenceEquals(t.Source, hostile)), Is.True);
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false), Is.False);
            Set(hostile, "mapIndexOrState", (sbyte)-1);
            Set(ticks, "ticksGameInt", 340);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void PeacefulAnimalDoesNotEnterThreatCache()
        {
            PrepareCombatFixture();
            Pawn animal = MakeCombatPawn("animal");
            Assert.That(animal.HostileTo(pawn), Is.False);
            Assert.That(ReadThreats(100).Count, Is.Zero);
        }

        [Test]
        public void HostileTurretIsDetectedBeyondConfiguredPawnRadius()
        {
            PrepareCombatFixture();
            TestTurret turret = MakeTurret(new IntVec3(50, 0, 5));
            List<ThreatAwareThreat> threats = ReadThreats(100);
            Assert.That(threats.Count, Is.EqualTo(1));
            Assert.That(threats[0].Source, Is.SameAs(turret));
            Assert.That(threats[0].ThreatensCell(map, new IntVec3(20, 0, 5), 15f), Is.True);
            Assert.That(threats[0].ThreatensCell(map, new IntVec3(10, 0, 5), 15f), Is.False);
        }

        [Test]
        public void FriendlyTurretDoesNotBlockOutdoorWork()
        {
            PrepareCombatFixture();
            MakeTurret(new IntVec3(2, 0, 1), friendly: true);
            Assert.That(ReadThreats(100).Count, Is.Zero);
        }

        [Test]
        public void DestroyedAndUnpoweredTurretsReleaseRestrictionAfterExpiry()
        {
            PrepareCombatFixture();
            TestTurret turret = MakeTurret(new IntVec3(2, 0, 1));
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            var power = Bare<CompPowerTrader>();
            power.parent = turret;
            turret.AllComps.Add(power); // default PowerOn=false; vanilla ThreatDisabled handles it.
            Set(ticks, "ticksGameInt", 340);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
            turret.AllComps.Clear();
            turret.AllComps.Add(new DummyTurretComp());
            ThreatAwareOutdoorRetryCooldown.Remember(pawn, Outside, 15f, 340);
            Set(ticks, "ticksGameInt", 460);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Set(turret, "mapIndexOrState", (sbyte)-1);
            Set(ticks, "ticksGameInt", 580);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void TurretShotGeometryIsSharedAndRecheckedOnExpiry()
        {
            PrepareCombatFixture();
            TestTurret turret = MakeTurret(new IntVec3(2, 0, 1));
            var shot = (CountingShot)turret.verb;
            ThreatAwareThreat threat = ReadThreats(100)[0];
            for (int i = 0; i < 100; i++) Assert.That(threat.ThreatensCell(map, Outside, 15f), Is.True);
            Assert.That(shot.hits, Is.EqualTo(1));
            shot.canHit = false;
            Set(ticks, "ticksGameInt", 220);
            Assert.That(threat.ThreatensCell(map, Outside, 15f), Is.False);
            Assert.That(shot.hits, Is.EqualTo(2));
        }

        [Test]
        public void ActualTurretVerbMinimumRangeIsRespected()
        {
            PrepareCombatFixture();
            TestTurret turret = MakeTurret(new IntVec3(2, 0, 1), realVerb: true);
            turret.verb.verbProps.minRange = 5f;
            ThreatAwareThreat threat = ReadThreats(100)[0];
            Assert.That(threat.ThreatensCell(map, Outside, 15f), Is.False);
            Assert.That(threat.ThreatensCell(map, new IntVec3(10, 0, 1), 15f), Is.True);
        }
        [Test]
        public void TurretNarrowFiringLaneOnUnsampledRouteCellIsDetected()
        {
            PrepareCombatFixture();
            var start = new IntVec3(15, 0, 5);
            var exposed = new IntVec3(18, 0, 5);
            homeGrid[start] = true;
            Set(pawn, "positionInt", start);
            TestTurret turret = MakeTurret(new IntVec3(50, 0, 5));
            ((CountingShot)turret.verb).onlyCell = exposed;
            var route = new List<IntVec3> { new IntVec3(16, 0, 5), new IntVec3(17, 0, 5), exposed, new IntVec3(19, 0, 5) };
            var args = new object[] { pawn, route, map.areaManager.Home, ReadThreats(100), BetterRimAIMod.Settings, null, null, 0f, IntVec3.Invalid, 0f };
            Assert.That(AccessTools.Method(typeof(ThreatAwareOutdoorWorkPatch), "TryFindUnsafeThreat").Invoke(null, args), Is.True);
            Assert.That(args[5], Is.SameAs(turret));
            Assert.That(args[8], Is.EqualTo(exposed));
        }

        [Test]
        public void WarmCandidateGateDoesNotRescanTurretsOrRecomputeFiringGeometry()
        {
            PrepareCombatFixture();
            TestTurret turret = MakeTurret(new IntVec3(2, 0, 1));
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            int hitChecks = ((CountingShot)turret.verb).hits;
            map.mapPawns = null;
            map.attackTargetsCache = null;
            for (int i = 0; i < 100000; i++)
            {
                if (!ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false)
                    || ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, indoor, false)) Assert.Fail("warm gate changed decision");
            }
            Assert.That(((CountingShot)turret.verb).hits, Is.EqualTo(hitChecks));
        }

        [Test]
        public void CalmedAggressiveAnimalReleasesOutdoorRestriction()
        {
            PrepareCombatFixture();
            Pawn animal = MakeCombatPawn("manhunter");
            Set(ticks, "ticksGameInt", 220);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.True);
            Set(animal.mindState.mentalStateHandler, "curStateInt", null);
            Set(ticks, "ticksGameInt", 340);
            Assert.That(ThreatAwareOutdoorWorkPatch.CouldBeBlockedThing(pawn, outdoor, false), Is.False);
        }

        [Test]
        public void ActualTurretVerbWallBlocksFireAndExpiryRechecksOpening()
        {
            PrepareCombatFixture();
            map.edificeGrid = Bare<EdificeGrid>();
            Set(map.edificeGrid, "map", map);
            var buildings = new Building[map.Size.x * map.Size.z];
            Set(map.edificeGrid, "innerArray", buildings);
            TestTurret turret = MakeTurret(new IntVec3(2, 0, 1), realVerb: true);
            turret.verb.verbProps.requireLineOfSight = true;
            var wall = new Building { def = Bare<ThingDef>() };
            wall.def.fillPercent = 1f;
            buildings[map.cellIndices.CellToIndex(new IntVec3(6, 0, 1))] = wall;
            ThreatAwareThreat threat = ReadThreats(100)[0];
            var target = new IntVec3(10, 0, 1);
            Assert.That(threat.ThreatensCell(map, target, 15f), Is.False);
            buildings[map.cellIndices.CellToIndex(new IntVec3(6, 0, 1))] = null;
            Set(ticks, "ticksGameInt", 220);
            Assert.That(threat.ThreatensCell(map, target, 15f), Is.True);
        }
    }
}
