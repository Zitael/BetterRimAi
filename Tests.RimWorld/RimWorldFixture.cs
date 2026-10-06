using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace BetterRimAI.RimWorldTests
{
    /// <summary>A minimal headless map/pawn, built from uninitialized RimWorld objects.</summary>
    internal sealed class HeadlessColony
    {
        internal static readonly IntVec3 Inside = new IntVec3(5, 0, 5);
        internal static readonly IntVec3 Outside = new IntVec3(1, 0, 1);

        internal readonly Game Game;
        internal readonly TickManager Ticks;
        internal readonly Map Map;
        internal readonly BoolGrid HomeGrid;
        internal readonly Faction Player;
        internal readonly Pawn Pawn;
        private readonly Game previousGame;
        private readonly BetterRimAISettings previousSettings;

        internal static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        internal static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        private static void SetCurrent(Game game) => AccessTools.Field(typeof(Current), "gameInt").SetValue(null, game);

        internal HeadlessColony()
        {
            LogCapture.Clear();
            previousGame = Current.Game;
            previousSettings = BetterRimAIMod.Settings;
            Game = Bare<Game>();
            Ticks = Bare<TickManager>();
            Set(Ticks, "ticksGameInt", 100);
            Game.tickManager = Ticks;
            Map = Bare<Map>();
            Map.uniqueID = 71;
            var info = Bare<MapInfo>();
            Set(info, "sizeInt", new IntVec3(80, 1, 80));
            Set(Map, "info", info);
            Map.cellIndices = new CellIndices(Map);
            Set(Game, "maps", new List<Map> { Map });
            var world = Bare<World>();
            world.factionManager = Bare<FactionManager>();
            Player = Bare<Faction>();
            Player.def = new FactionDef { isPlayer = true };
            Set(world.factionManager, "ofPlayer", Player);
            Set(Game, "worldInt", world);
            SetCurrent(Game);
            BetterRimAIMod.Settings = new BetterRimAISettings { threatAwareOutdoorWork = true, threatDebugLogging = false };
            new ThreatAwareGameState(Game);

            Map.areaManager = Bare<AreaManager>();
            Set(Map.areaManager, "map", Map);
            var home = Bare<Area_Home>();
            Set(home, "areaManager", Map.areaManager);
            HomeGrid = new BoolGrid(Map);
            HomeGrid[Inside] = true;
            Set(home, "innerGrid", HomeGrid);
            Set(Map.areaManager, "areas", new List<Area> { home });
            Map.attackTargetsCache = Bare<AttackTargetsCache>();
            Set(Map.attackTargetsCache, "map", Map);
            Set(Map.attackTargetsCache, "allTargets", new HashSet<IAttackTarget>());
            Set(Map.attackTargetsCache, "targetsHostileToFaction", new Dictionary<Faction, HashSet<IAttackTarget>>
            {
                [Player] = new HashSet<IAttackTarget>()
            });

            Pawn = Bare<Pawn>();
            Pawn.thingIDNumber = 17;
            Pawn.def = Bare<ThingDef>();
            Pawn.def.race = new RaceProperties { intelligence = Intelligence.Humanlike };
            Pawn.kindDef = new PawnKindDef();
            Pawn.health = Bare<Pawn_HealthTracker>();
            Set(Pawn.health, "healthState", PawnHealthState.Mobile);
            Pawn.mindState = Bare<Pawn_MindState>();
            Pawn.mindState.mentalStateHandler = Bare<MentalStateHandler>();
            Set(Pawn.mindState, "pawn", Pawn);
            Set(Pawn.mindState.mentalStateHandler, "pawn", Pawn);
            Set(Pawn, "factionInt", Player);
            Set(Pawn, "mapIndexOrState", (sbyte)0);
            Set(Pawn, "positionInt", Inside);
            Pawn.drafter = Bare<Pawn_DraftController>();
            Pawn.playerSettings = Bare<Pawn_PlayerSettings>();
            Set(Pawn.playerSettings, "pawn", Pawn);
            Pawn.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
        }

        internal Thing MakeThing(int id, IntVec3 cell)
        {
            var thing = Bare<Thing>();
            thing.thingIDNumber = id;
            thing.def = Bare<ThingDef>();
            thing.def.size = new IntVec2(1, 1);
            Set(thing, "mapIndexOrState", (sbyte)0);
            Set(thing, "positionInt", cell);
            return thing;
        }

        internal void Dispose()
        {
            new ThreatAwareGameState(Game);
            SetCurrent(previousGame);
            BetterRimAIMod.Settings = previousSettings;
        }
    }
}
