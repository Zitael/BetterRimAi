using System;
using System.Collections.Generic;

namespace BetterRimAI.Tests
{
    /// <summary>
    /// ASCII test map for the pure threat geometry. Row 0 of the picture is the top (highest z).
    /// <code>
    ///   .  exterior floor          #  exterior wall / rock
    ///   H  Home floor              h  Home wall (painted, impassable)
    ///   o  unpainted floor         D  closed door in Home
    ///   E  hostile on exterior floor
    ///   P  pawn on Home floor
    ///   A..Z (other letters) named exterior floor cells for assertions, a..z named Home floor
    /// </code>
    /// </summary>
    internal sealed class GridMap : IDistanceFields
    {
        internal readonly int Width, Height;
        internal readonly bool[] Home, Walkable, Passable, IsProtected;
        internal readonly int[] Component;
        internal readonly byte[] Danger;
        internal readonly List<int> Hostiles = new List<int>();
        private readonly Dictionary<char, int> named = new Dictionary<char, int>();
        private readonly int[] queue, scratch;
        private readonly Dictionary<int, int[]> direct = new Dictionary<int, int[]>(), safe = new Dictionary<int, int[]>();
        internal int FieldRequests;

        internal GridMap(params string[] rows)
        {
            Height = rows.Length;
            Width = rows[0].Length;
            int count = Width * Height;
            Home = new bool[count];
            Walkable = new bool[count];
            Passable = new bool[count];
            IsProtected = new bool[count];
            Component = new int[count];
            Danger = new byte[count];
            queue = new int[count];
            scratch = new int[count];
            for (int row = 0; row < Height; row++)
            {
                if (rows[row].Length != Width) throw new ArgumentException("ragged map");
                for (int x = 0; x < Width; x++)
                {
                    char c = rows[row][x];
                    int i = (Height - 1 - row) * Width + x;
                    switch (c)
                    {
                        case '.': Walkable[i] = Passable[i] = true; break;
                        case '#': break;
                        case 'H': Home[i] = Walkable[i] = Passable[i] = true; break;
                        case 'h': Home[i] = true; break;
                        case 'o': Walkable[i] = Passable[i] = true; break;
                        case 'D': Home[i] = Walkable[i] = true; break;
                        case 'E': Walkable[i] = Passable[i] = true; Hostiles.Add(i); break;
                        case 'P': Home[i] = Walkable[i] = Passable[i] = true; named['P'] = i; break;
                        default:
                            if (char.IsUpper(c)) Walkable[i] = Passable[i] = true;
                            else if (char.IsLower(c)) Home[i] = Walkable[i] = Passable[i] = true;
                            else throw new ArgumentException("unknown cell " + c);
                            named[c] = i;
                            break;
                    }
                }
            }
            Rebuild();
        }

        internal int this[char name] => named[name];

        internal int At(int x, int z) => z * Width + x;

        /// <summary>Recomputes envelope, components and danger, as MapThreatState does on refresh.</summary>
        internal GridMap Rebuild(int radius = 18)
        {
            ThreatGeometry.BuildEnvelope(Home, Width, Height, IsProtected, queue);
            ThreatGeometry.LabelComponents(IsProtected, Walkable, Width, Height, Component, queue);
            Array.Clear(Danger, 0, Danger.Length);
            ThreatGeometry.SpreadDanger(Hostiles.ToArray(), Hostiles.Count, Passable, Width, Height, radius, Danger, scratch, queue);
            direct.Clear();
            safe.Clear();
            return this;
        }

        internal Verdict Decide(int pawnCell, params int[] workCells)
            => ThreatGeometry.Decide(workCells, workCells.Length, Component[pawnCell], Component, IsProtected, Danger, this, out _);

        public int[] Direct(int component) => Field(direct, component, null);

        public int[] Safe(int component) => Field(safe, component, Danger);

        private int[] Field(Dictionary<int, int[]> cache, int component, byte[] avoid)
        {
            FieldRequests++;
            if (cache.TryGetValue(component, out int[] field)) return field;
            field = new int[Width * Height];
            ThreatGeometry.Distances(Component, component, Walkable, avoid, Width, Height, field, queue);
            cache[component] = field;
            return field;
        }
    }
}
