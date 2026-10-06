using System;

namespace BetterRimAI
{
    /// <summary>
    /// Pure grid algorithms used by threat safety. Cells are map indices (z * width + x).
    /// Nothing here touches RimWorld types, so it can be unit-tested headlessly; the RimWorld
    /// adapter (<see cref="MapThreatState"/>) fills the input grids from the live map.
    /// </summary>
    internal static class ThreatGeometry
    {
        internal const int Unreached = int.MaxValue;
        internal const int MinDetourSlack = 8;

        /// <summary>
        /// Protected envelope: painted Home plus every non-Home pocket that cannot reach the
        /// map edge without crossing Home. Unpainted floor inside a painted base stays protected.
        /// </summary>
        internal static void BuildEnvelope(bool[] home, int width, int height, bool[] isProtected, int[] queue)
        {
            int count = width * height;
            // isProtected doubles as "visited exterior" during the flood, then gets inverted.
            for (int i = 0; i < count; i++) isProtected[i] = false;
            int head = 0, tail = 0;
            for (int x = 0; x < width; x++)
            {
                SeedExterior(home, isProtected, queue, ref tail, x);
                SeedExterior(home, isProtected, queue, ref tail, (height - 1) * width + x);
            }
            for (int z = 1; z < height - 1; z++)
            {
                SeedExterior(home, isProtected, queue, ref tail, z * width);
                SeedExterior(home, isProtected, queue, ref tail, z * width + width - 1);
            }
            while (head < tail)
            {
                int i = queue[head++], x = i % width;
                if (x > 0) SeedExterior(home, isProtected, queue, ref tail, i - 1);
                if (x + 1 < width) SeedExterior(home, isProtected, queue, ref tail, i + 1);
                if (i >= width) SeedExterior(home, isProtected, queue, ref tail, i - width);
                if (i + width < count) SeedExterior(home, isProtected, queue, ref tail, i + width);
            }
            for (int i = 0; i < count; i++) isProtected[i] = !isProtected[i];
        }

        private static void SeedExterior(bool[] home, bool[] exterior, int[] queue, ref int tail, int i)
        {
            if (exterior[i] || home[i]) return;
            exterior[i] = true;
            queue[tail++] = i;
        }

        /// <summary>
        /// Labels 4-connected components of protected walkable cells (1..n, 0 = none).
        /// RimWorld forbids diagonal moves past an impassable corner, so 4-connectivity matches
        /// which protected cells a pawn can walk between without leaving protected space.
        /// </summary>
        internal static int LabelComponents(bool[] isProtected, bool[] walkable, int width, int height, int[] component, int[] queue)
        {
            int count = width * height;
            for (int i = 0; i < count; i++) component[i] = 0;
            int next = 0;
            for (int start = 0; start < count; start++)
            {
                if (component[start] != 0 || !isProtected[start] || !walkable[start]) continue;
                int id = ++next, head = 0, tail = 0;
                component[start] = id;
                queue[tail++] = start;
                while (head < tail)
                {
                    int i = queue[head++], x = i % width;
                    if (x > 0) Join(i - 1);
                    if (x + 1 < width) Join(i + 1);
                    if (i >= width) Join(i - width);
                    if (i + width < count) Join(i + width);
                }

                void Join(int n)
                {
                    if (component[n] != 0 || !isProtected[n] || !walkable[n]) return;
                    component[n] = id;
                    queue[tail++] = n;
                }
            }
            return next;
        }

        /// <summary>
        /// Marks cells within <paramref name="radius"/> walking steps of any source. Danger only
        /// spreads through passable cells (walls and closed doors stop it), so a sealed room's
        /// occupants do not make the terrain behind its walls dangerous.
        /// </summary>
        internal static void SpreadDanger(int[] sources, int sourceCount, bool[] passable, int width, int height,
            int radius, byte[] danger, int[] distance, int[] queue)
        {
            int count = width * height;
            for (int i = 0; i < count; i++) distance[i] = Unreached;
            int head = 0, tail = 0;
            for (int s = 0; s < sourceCount; s++)
            {
                int i = sources[s];
                if (i < 0 || i >= count || distance[i] == 0) continue;
                distance[i] = 0;
                danger[i] = 1;
                queue[tail++] = i;
            }
            while (head < tail)
            {
                int i = queue[head++];
                int d = distance[i] + 1;
                if (d > radius) continue;
                int x = i % width;
                if (x > 0) Step(i - 1, d);
                if (x + 1 < width) Step(i + 1, d);
                if (i >= width) Step(i - width, d);
                if (i + width < count) Step(i + width, d);
            }

            void Step(int n, int d)
            {
                if (distance[n] <= d || !passable[n]) return;
                distance[n] = d;
                danger[n] = 1;
                queue[tail++] = n;
            }
        }

        /// <summary>
        /// Walking distance from every cell of component <paramref name="componentId"/>. With
        /// <paramref name="avoid"/> set, flagged cells are never entered (or used as seeds), which
        /// yields the shortest danger-free route.
        /// </summary>
        internal static void Distances(int[] component, int componentId, bool[] walkable, byte[] avoid,
            int width, int height, int[] distance, int[] queue)
        {
            int count = width * height;
            int head = 0, tail = 0;
            for (int i = 0; i < count; i++)
            {
                if (component[i] == componentId && (avoid == null || avoid[i] == 0))
                {
                    distance[i] = 0;
                    queue[tail++] = i;
                }
                else distance[i] = Unreached;
            }
            while (head < tail)
            {
                int i = queue[head++];
                int d = distance[i] + 1, x = i % width;
                if (x > 0) Step(i - 1, d);
                if (x + 1 < width) Step(i + 1, d);
                if (i >= width) Step(i - width, d);
                if (i + width < count) Step(i + width, d);
            }

            void Step(int n, int d)
            {
                if (distance[n] != Unreached || !walkable[n] || (avoid != null && avoid[n] != 0)) return;
                distance[n] = d;
                queue[tail++] = n;
            }
        }

        /// <summary>
        /// Vanilla walks the shortest route, not BetterRimAI's danger-free one. A safe route
        /// only counts when it is not a detour vanilla would never take.
        /// </summary>
        internal static bool SafeRouteIsRealistic(int safeDistance, int directDistance)
        {
            if (safeDistance == Unreached || directDistance == Unreached) return false;
            return safeDistance <= directDistance + Math.Max(MinDetourSlack, directDistance / 4);
        }

        /// <summary>Exposure of one work cell outside the pawn's protected component, given both distance fields.</summary>
        internal static Exposure ClassifyExteriorCell(int cell, byte[] danger, int[] direct, int[] safe)
        {
            if (direct[cell] == Unreached) return Exposure.Unreachable;
            if (danger[cell] != 0) return Exposure.DangerAtWorkCell;
            if (safe[cell] == Unreached) return Exposure.NoSafeRoute;
            return SafeRouteIsRealistic(safe[cell], direct[cell]) ? Exposure.Safe : Exposure.RouteThroughDanger;
        }

        /// <summary>
        /// The decision for one autonomous work candidate, given the cells from which vanilla
        /// would perform it. Flood fields are requested only when exterior cells are involved.
        /// </summary>
        internal static Verdict Decide(int[] workCells, int workCellCount, int pawnComponent, int[] component,
            bool[] isProtected, byte[] danger, IDistanceFields fields, out int safeSideCell)
        {
            safeSideCell = -1;
            bool sameComponent = false, away = false;
            for (int k = 0; k < workCellCount; k++)
            {
                int c = workCells[k];
                if (pawnComponent != 0 && component[c] == pawnComponent)
                {
                    if (!sameComponent) safeSideCell = c;
                    sameComponent = true;
                }
                else away = true;
            }
            if (!away) return sameComponent ? Verdict.Interior : Verdict.NoWorkCell;

            // Any cell outside the pawn's own protected component means walking out: through
            // open exterior, or across it to a separate patch of Home (an auto-home ring around
            // an outer wall or hull). That walk must be realistically safe.
            int[] directField = fields.Direct(pawnComponent);
            int[] safeField = fields.Safe(pawnComponent);
            bool anySafe = false;
            Exposure worst = Exposure.Unreachable;
            for (int k = 0; k < workCellCount; k++)
            {
                int c = workCells[k];
                if (pawnComponent != 0 && component[c] == pawnComponent) continue;
                Exposure e = ClassifyExteriorCell(c, danger, directField, safeField);
                if (e == Exposure.Safe) anySafe = true;
                else if (e != Exposure.Unreachable && worst == Exposure.Unreachable) worst = e;
            }
            if (sameComponent)
                // Vanilla could pick an exposed outside Touch cell. Only steer to the protected
                // side when that outside side is actually unsafe.
                return worst != Exposure.Unreachable ? Verdict.SafeSide : Verdict.Interior;
            if (anySafe) return Verdict.ExteriorSafe;
            switch (worst)
            {
                case Exposure.DangerAtWorkCell: return Verdict.RejectDangerAtWorkCell;
                case Exposure.NoSafeRoute: return Verdict.RejectNoSafeRoute;
                case Exposure.RouteThroughDanger: return Verdict.RejectRouteThroughDanger;
                default: return Verdict.NoWorkCell;
            }
        }
    }

    /// <summary>Lazily computed distance fields from one protected component.</summary>
    internal interface IDistanceFields
    {
        int[] Direct(int component);
        int[] Safe(int component);
    }

    internal enum Exposure : byte
    {
        Unreachable,
        Safe,
        DangerAtWorkCell,
        NoSafeRoute,
        RouteThroughDanger
    }

    internal enum Verdict : byte
    {
        /// <summary>Nothing BetterRimAI can judge (no usable work cell). Vanilla decides.</summary>
        NoWorkCell,
        /// <summary>The pawn can do the work without leaving its protected area.</summary>
        Interior,
        /// <summary>Work outside the pawn's protected area, but its cell and realistic route are clear of danger.</summary>
        ExteriorSafe,
        /// <summary>A protected work cell exists while the exterior side is unsafe; steer the path there.</summary>
        SafeSide,
        RejectDangerAtWorkCell,
        RejectNoSafeRoute,
        RejectRouteThroughDanger
    }

    internal static class VerdictExtensions
    {
        internal static bool IsReject(this Verdict v) => v >= Verdict.RejectDangerAtWorkCell;

        internal static string Describe(this Verdict v)
        {
            switch (v)
            {
                case Verdict.RejectDangerAtWorkCell: return "requires unsafe protected->outside transition (work cell in danger)";
                case Verdict.RejectNoSafeRoute: return "requires unsafe protected->outside transition (no danger-free route)";
                case Verdict.RejectRouteThroughDanger: return "requires unsafe protected->outside transition (vanilla route passes danger)";
                case Verdict.SafeSide: return "protected interaction cell available";
                case Verdict.ExteriorSafe: return "exterior work clear of danger";
                case Verdict.Interior: return "work performed inside protected area";
                default: return "no usable work cell";
            }
        }
    }
}
