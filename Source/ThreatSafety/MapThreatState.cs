using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace BetterRimAI
{
    /// <summary>
    /// One threat snapshot per map, shared by every pawn. With no active hostile threat the
    /// refresh is a walk over vanilla's (usually empty) hostile-target set and nothing else is
    /// built: <see cref="Active"/> is false and every threat-safety hook returns immediately.
    /// While threats are active the snapshot holds the protected envelope, its walkable
    /// components, the danger grid and lazily computed distance fields.
    /// </summary>
    internal sealed class MapThreatState : IDistanceFields
    {
        internal const int RefreshTicks = 120;
        private const int TurretCacheTicks = 1250;
        private const float MaxTurretRange = 45f;

        private static ConditionalWeakTable<Map, MapThreatState> states = new ConditionalWeakTable<Map, MapThreatState>();
        private static readonly ConditionalWeakTable<Map, MapThreatState>.CreateValueCallback Create = m => new MapThreatState(m);

        internal static void Reset() => states = new ConditionalWeakTable<Map, MapThreatState>();

        /// <summary>The map's state, refreshed when older than <see cref="RefreshTicks"/>.</summary>
        internal static MapThreatState For(Map map)
        {
            MapThreatState state = states.GetValue(map, Create);
            state.RefreshIfStale();
            return state;
        }

        internal readonly Map Map;
        private int refreshedTick = int.MinValue;
        private bool active;
        private int generation;

        internal int Width, Height;
        internal bool[] IsProtected;
        internal bool[] Walkable;
        private bool[] passable;
        internal int[] Component;
        internal byte[] Danger;
        private int[] queue, scratch;

        internal int ThreatCount { get; private set; }
        internal Thing ExampleThreat { get; private set; }

        private readonly List<Thing> mobile = new List<Thing>();
        private readonly List<Thing> turrets = new List<Thing>();
        private int[] sources = new int[16];

        private sealed class Field
        {
            internal int generation = -1;
            internal int[] distance;
        }
        private readonly Dictionary<int, Field> fields = new Dictionary<int, Field>();

        private sealed class TurretCells
        {
            internal int position, tick, seen;
            internal float range;
            internal readonly List<int> cells = new List<int>();
        }
        private readonly Dictionary<int, TurretCells> turretCells = new Dictionary<int, TurretCells>();
        private readonly List<int> staleTurrets = new List<int>();

        private MapThreatState(Map map) => Map = map;

        internal bool Active => active;

        internal int Index(IntVec3 c) => c.z * Width + c.x;

        internal int ComponentAt(IntVec3 c) => active && c.InBounds(Map) ? Component[Index(c)] : 0;

        internal bool AllProtected(CellRect rect)
        {
            int minX = Math.Max(0, rect.minX), maxX = Math.Min(Width - 1, rect.maxX);
            int minZ = Math.Max(0, rect.minZ), maxZ = Math.Min(Height - 1, rect.maxZ);
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                    if (!IsProtected[z * Width + x]) return false;
            return true;
        }

        public int[] Direct(int component) => DistanceField(component, false);

        public int[] Safe(int component) => DistanceField(component, true);

        private int[] DistanceField(int component, bool avoidDanger)
        {
            int key = component * 2 + (avoidDanger ? 1 : 0);
            if (!fields.TryGetValue(key, out Field field))
            {
                field = new Field();
                fields.Add(key, field);
            }
            int count = Width * Height;
            if (field.generation == generation && field.distance != null && field.distance.Length == count)
                return field.distance;
            if (field.distance == null || field.distance.Length != count) field.distance = new int[count];
            ThreatGeometry.Distances(Component, component, Walkable, avoidDanger ? Danger : null,
                Width, Height, field.distance, queue);
            field.generation = generation;
            return field.distance;
        }

        private void RefreshIfStale()
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (refreshedTick != int.MinValue && tick >= refreshedTick && tick - refreshedTick < RefreshTicks) return;
            refreshedTick = tick;
            bool wasActive = active;
            try
            {
                active = Rebuild(tick);
            }
            catch (Exception ex)
            {
                // Fail open: an exception must never leave a stale restriction behind.
                active = false;
                Log.ErrorOnce("[BetterRimAI] Threat snapshot failed; threat safety is idle for this map: " + ex,
                    0x5AFE0000 ^ Map.uniqueID);
            }
            if (wasActive != active) ThreatSafetyLog.StateChanged(Map, active, ThreatCount, ExampleThreat);
        }

        private bool Rebuild(int tick)
        {
            mobile.Clear();
            turrets.Clear();
            ExampleThreat = null;
            ThreatCount = 0;
            HashSet<IAttackTarget> hostiles = Map.attackTargetsCache?.TargetsHostileToColony;
            if (hostiles == null || hostiles.Count == 0) return false;

            foreach (IAttackTarget target in hostiles)
            {
                Thing thing = target?.Thing;
                if (thing == null || !thing.Spawned || thing.Map != Map) continue;
                // Vanilla's notion of an active threat: hostile, able to attack, not disabled,
                // dormant or downed, and able to reach unfogged space (a sealed, unopened
                // complex is fogged). Hive defenders count: they attack pawns that approach.
                if (!GenHostility.IsActiveThreatTo(target, Faction.OfPlayer, ignoreHives: false, canBeFogged: false)) continue;
                if (thing is Pawn) mobile.Add(thing);
                else
                {
                    Verb verb = (thing as IAttackTargetSearcher)?.CurrentEffectiveVerb;
                    if (verb == null || verb.IsMeleeAttack) continue;
                    turrets.Add(thing);
                }
                if (ExampleThreat == null) ExampleThreat = thing;
            }
            ThreatCount = mobile.Count + turrets.Count;
            if (ThreatCount == 0) return false;

            EnsureGrids();
            int count = Width * Height;
            // Copy: the shared envelope cache may be rebuilt in place by other callers, and this
            // snapshot's components must stay consistent with the envelope they were built from.
            bool[] envelope = ProtectedArea.Get(Map, RefreshTicks);
            if (envelope == null) Array.Clear(IsProtected, 0, count);
            else Array.Copy(envelope, IsProtected, count);
            PathGrid pathGrid = Map.pathing.Normal.pathGrid;
            EdificeGrid edifices = Map.edificeGrid;
            for (int i = 0; i < count; i++)
            {
                bool walkable = pathGrid.WalkableFast(i);
                Walkable[i] = walkable;
                passable[i] = walkable && !(edifices[i] is Building_Door door && !door.Open);
                Danger[i] = 0;
            }
            ThreatGeometry.LabelComponents(IsProtected, Walkable, Width, Height, Component, queue);

            if (sources.Length < mobile.Count) sources = new int[mobile.Count * 2];
            for (int i = 0; i < mobile.Count; i++) sources[i] = Index(mobile[i].Position);
            int radius = (int)Math.Round(BetterRimAIMod.Settings?.threatRadius ?? BetterRimAISettings.DefaultThreatRadius);
            ThreatGeometry.SpreadDanger(sources, mobile.Count, passable, Width, Height, radius, Danger, scratch, queue);

            for (int i = 0; i < turrets.Count; i++) MarkTurret(turrets[i], tick);
            staleTurrets.Clear();
            foreach (KeyValuePair<int, TurretCells> entry in turretCells)
                if (entry.Value.seen != tick) staleTurrets.Add(entry.Key);
            for (int i = 0; i < staleTurrets.Count; i++) turretCells.Remove(staleTurrets[i]);

            generation++;
            return true;
        }

        private void EnsureGrids()
        {
            int width = Map.Size.x, height = Map.Size.z, count = width * height;
            if (Walkable != null && Walkable.Length == count && Width == width) return;
            Width = width;
            Height = height;
            IsProtected = new bool[count];
            Walkable = new bool[count];
            passable = new bool[count];
            Component = new int[count];
            Danger = new byte[count];
            queue = new int[count];
            scratch = new int[count];
            fields.Clear();
            turretCells.Clear();
        }

        /// <summary>
        /// Stationary attackers use their own verb: range, minimum range and line of fire
        /// (walls and other cover). Cells are cached per turret until it moves or the cache ages.
        /// </summary>
        private void MarkTurret(Thing turret, int tick)
        {
            Verb verb = (turret as IAttackTargetSearcher)?.CurrentEffectiveVerb;
            if (verb == null) return;
            float range = Math.Min(verb.EffectiveRange, MaxTurretRange);
            int position = Index(turret.Position);
            if (!turretCells.TryGetValue(turret.thingIDNumber, out TurretCells cache))
            {
                cache = new TurretCells();
                turretCells.Add(turret.thingIDNumber, cache);
                cache.tick = int.MinValue;
            }
            cache.seen = tick;
            bool fresh = cache.position == position && cache.range == range && cache.tick != int.MinValue
                         && tick >= cache.tick && tick - cache.tick < TurretCacheTicks;
            if (!fresh)
            {
                cache.cells.Clear();
                cache.position = position;
                cache.range = range;
                cache.tick = tick;
                IntVec3 origin = turret.Position;
                int r = (int)Math.Ceiling(range);
                float rangeSquared = range * range;
                try
                {
                    for (int z = Math.Max(0, origin.z - r); z <= Math.Min(Height - 1, origin.z + r); z++)
                        for (int x = Math.Max(0, origin.x - r); x <= Math.Min(Width - 1, origin.x + r); x++)
                        {
                            int dx = x - origin.x, dz = z - origin.z;
                            if (dx * dx + dz * dz > rangeSquared) continue;
                            if (verb.CanHitTargetFrom(origin, new LocalTargetInfo(new IntVec3(x, 0, z))))
                                cache.cells.Add(z * Width + x);
                        }
                }
                catch (Exception ex)
                {
                    cache.cells.Clear();
                    Log.WarningOnce("[BetterRimAI] Could not evaluate firing lanes of " + turret + ": " + ex,
                        0x5AFE1000 ^ turret.thingIDNumber);
                }
            }
            for (int i = 0; i < cache.cells.Count; i++) Danger[cache.cells[i]] = 1;
        }
    }
}
