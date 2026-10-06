using NUnit.Framework;

namespace BetterRimAI.Tests
{
    /// <summary>
    /// Headless tests of the pure threat geometry and decision rules. They do not exercise
    /// RimWorld's job lifecycle (JobGiver_Work, job drivers, pathing): the work cells passed to
    /// Decide stand in for what vanilla's Touch rule produces in game. See README "Validation".
    /// </summary>
    [TestFixture]
    public class ThreatGeometryTests
    {
        [Test]
        public void UnpaintedPocketInsideHomeIsProtected_ButEdgeConnectedGapIsNot()
        {
            var map = new GridMap(
                "..........",
                ".HHHHH....",
                ".HoHHH....",
                ".HHHHo....",
                "..........");
            Assert.That(map.IsProtected[map.At(2, 2)], Is.True, "enclosed unpainted floor stays protected");
            Assert.That(map.IsProtected[map.At(5, 1)], Is.False, "unpainted cell touching the exterior is outside");
            Assert.That(map.IsProtected[map.At(0, 0)], Is.False);
        }

        [Test]
        public void SeparateBuildingsAreSeparateComponents_DoorJoinsThem()
        {
            var map = new GridMap(
                "...........",
                ".hhh...hhh.",
                ".hah...hbh.",
                ".hhh...hhh.",
                "...........");
            Assert.That(map.Component[map['a']], Is.Not.EqualTo(0));
            Assert.That(map.Component[map['a']], Is.Not.EqualTo(map.Component[map['b']]));
            Assert.That(map.Component[map.At(0, 0)], Is.EqualTo(0), "exterior has no component");

            var joined = new GridMap(
                "hhhhhhh",
                "haHDHbh",
                "hhhhhhh");
            Assert.That(joined.Component[joined['a']], Is.EqualTo(joined.Component[joined['b']]),
                "a door is walkable for the colony's own pawns");
        }

        [Test]
        public void DangerRespectsRadiusAndDoesNotPassWallsOrClosedDoors()
        {
            var map = new GridMap(
                "...#....",
                ".E.#.A..",
                "...#....",
                "........");
            map.Rebuild(radius: 3);
            Assert.That(map.Danger[map.At(1, 2)], Is.EqualTo(1), "hostile cell");
            Assert.That(map.Danger[map.At(2, 0)], Is.EqualTo(1), "within 3 steps");
            Assert.That(map.Danger[map['A']], Is.EqualTo(0),
                "A is 4 cells away in a straight line but 8 walking steps around the wall");
            map.Rebuild(radius: 8);
            Assert.That(map.Danger[map['A']], Is.EqualTo(1));

            var door = new GridMap(
                "hhhhh",
                "hEDAh",
                "hhhhh");
            Assert.That(door.Danger[door['A']], Is.EqualTo(0), "closed door stops danger");
        }

        [Test]
        public void SealedComplexDoesNotProjectDanger_OpeningItDoes()
        {
            // Scenario G/H: dormant or active mechs in a sealed complex near the colony.
            string[] rows =
            {
                "..............",
                ".#####........",
                ".#.E.#..A..hDh",
                ".#####.....hPh",
                "...........hhh",
            };
            var sealedMap = new GridMap(rows);
            Assert.That(sealedMap.Danger[sealedMap['A']], Is.EqualTo(0));
            Assert.That(sealedMap.Decide(sealedMap['P'], sealedMap['A']), Is.EqualTo(Verdict.ExteriorSafe));

            rows[3] = ".##.##.....hPh";
            Assert.That(sealedMap.Component[sealedMap['P']], Is.Not.EqualTo(0));
            var opened = new GridMap(rows);
            Assert.That(opened.Danger[opened['A']], Is.EqualTo(1));
            Assert.That(opened.Decide(opened['P'], opened['A']).IsReject(), Is.True);
        }

        [Test]
        public void InteriorWorkNeverRequestsDistanceFields()
        {
            var map = new GridMap(
                "E.........",
                "..hhhhhh..",
                "..hPabch..",
                "..hhhhhh..");
            Assert.That(map.Decide(map['P'], map['a'], map['b'], map['c']), Is.EqualTo(Verdict.Interior));
            Assert.That(map.FieldRequests, Is.EqualTo(0));
        }

        [Test]
        public void ExteriorOnlyWorkNearHostileIsRejectedBeforeMovement()
        {
            // Scenario C/D: the only work cells are outside, next to the hostile.
            var map = new GridMap(
                "..........",
                ".EA.......",
                "...hhhhh..",
                "...hPHHD..",
                "...hhhhh..");
            Assert.That(map.Decide(map['P'], map['A']), Is.EqualTo(Verdict.RejectDangerAtWorkCell));
        }

        [Test]
        public void HostileOnTheDirectRouteRejectsEvenIfAHugeDetourExists()
        {
            // The old snapshot accepted any danger-free route; vanilla then walked the direct one,
            // the path guard stopped it, and the same Repair was selected again.
            var rows = new string[21];
            for (int z = 0; z < rows.Length; z++) rows[z] = new string('.', 30);
            rows[9] = "hhhhh" + new string('.', 25);
            rows[10] = "hPHHD...........E...........A.";
            rows[11] = "hhhhh" + new string('.', 25);
            var map = new GridMap(rows);

            map.Rebuild(radius: 2);
            Assert.That(map.Danger[map['A']], Is.EqualTo(0));
            Assert.That(map.Decide(map['P'], map['A']), Is.EqualTo(Verdict.ExteriorSafe),
                "a short side-step around a small danger zone is a route vanilla would plausibly take");

            map.Rebuild(radius: 8);
            Assert.That(map.Danger[map['A']], Is.EqualTo(0), "the work cell itself is clear");
            Assert.That(map.Decide(map['P'], map['A']), Is.EqualTo(Verdict.RejectRouteThroughDanger),
                "the only danger-free way round is far longer than vanilla's direct route");

            map.Rebuild(radius: 12);
            Assert.That(map.Decide(map['P'], map['A']).IsReject(), Is.True, "danger reaches the exit and the work cell");
        }

        [Test]
        public void SafeSideOnlyWhenTheExteriorSideIsUnsafe()
        {
            // Scenario E: outer wall (#) repaired from the inside cell i or the outside cell O.
            string[] rows =
            {
                "...............",
                "..O............",
                "hh#hh..........",
                "hPiHD..........",
                "hhhhh..........",
            };
            var calm = new GridMap(rows);
            Assert.That(calm.Decide(calm['P'], calm['i'], calm['O']), Is.EqualTo(Verdict.Interior),
                "no danger outside: no reason to steer vanilla's choice");

            rows[0] = "..E............";
            var raid = new GridMap(rows);
            int safeCell;
            Verdict v = ThreatGeometry.Decide(new[] { raid['i'], raid['O'] }, 2, raid.Component[raid['P']], raid.Component,
                raid.IsProtected, raid.Danger, raid, out safeCell);
            Assert.That(v, Is.EqualTo(Verdict.SafeSide));
            Assert.That(safeCell, Is.EqualTo(raid['i']));
        }

        [Test]
        public void BlockedInteriorSideMeansDepartureIsJudged()
        {
            // Scenario D: an engine occupies the interior cell, so vanilla's Touch rule yields only
            // the exterior cell O. The wall itself is painted Home (auto-home around buildings);
            // the old target-coordinate rule called this "target protected" and allowed it.
            string[] rows =
            {
                "..E............",
                "..O............",
                "hh#hh..........",
                "hH#HD..........",
                "hPHHh..........",
                "hhhhh..........",
            };
            var map = new GridMap(rows);
            Assert.That(map.Decide(map['P'], map['O']).IsReject(), Is.True);
        }

        [Test]
        public void RoofCornerWithNoInteriorTouchCellIsJudgedByItsExteriorCells()
        {
            // A roof over a corner wall: vanilla forbids touching it diagonally from the inside
            // corner, so the work cells are the two exterior cells only. The old safe-cell finder
            // ignored the corner rule, sent the pawn to the interior diagonal, and the job driver
            // failed the touch check on arrival: 10 BuildRoof jobs per tick, even with no threat.
            string[] rows =
            {
                "........",
                ".AB.....",
                ".Chhhh..",
                "..hPHD..",
                "..hhhh..",
            };
            var calm = new GridMap(rows);
            Assert.That(calm.Decide(calm['P'], calm['B'], calm['C']), Is.EqualTo(Verdict.ExteriorSafe));

            rows[0] = "E.......";
            var raid = new GridMap(rows);
            Assert.That(raid.Decide(raid['P'], raid['B'], raid['C']).IsReject(), Is.True);
        }

        [Test]
        public void ThreatGoneMeansNoRestrictionAndNoStaleState()
        {
            // Scenario K: the decision is a pure function of the current snapshot.
            string[] rows =
            {
                ".E.A......",
                "...hhhhh..",
                "...hPHHD..",
                "...hhhhh..",
            };
            var raid = new GridMap(rows);
            Assert.That(raid.Decide(raid['P'], raid['A']).IsReject(), Is.True);
            raid.Hostiles.Clear();
            raid.Rebuild();
            Assert.That(raid.Decide(raid['P'], raid['A']), Is.EqualTo(Verdict.ExteriorSafe));
        }

        [Test]
        public void PawnOutsideProtectedSpaceIsNeverSteeredToASafeSide()
        {
            var map = new GridMap(
                "..E.......",
                "..O.......",
                "hh#hh.....",
                "hHiHh.....",
                "hhhhh.....",
                "Q.........");
            Assert.That(map.Component[map['Q']], Is.EqualTo(0));
            Verdict v = map.Decide(map['Q'], map['i'], map['O']);
            Assert.That(v, Is.Not.EqualTo(Verdict.SafeSide));
        }

        [Test]
        public void SeparateHomePatchCountsAsLeavingSafety()
        {
            // Auto-home paints a ring around outer walls and hulls. Reaching such a patch means
            // walking outside, so it is judged like any outside work cell.
            string[] rows =
            {
                "...........",
                "hhh...hhh..",
                "hPh...hbh..",
                "hHD...DHh..",
                "hhh...hhh..",
            };
            var calm = new GridMap(rows);
            Assert.That(calm.Component[calm['b']], Is.Not.EqualTo(calm.Component[calm['P']]));
            Assert.That(calm.Decide(calm['P'], calm['b']), Is.EqualTo(Verdict.ExteriorSafe));

            rows[0] = "....E......";
            var raid = new GridMap(rows);
            Assert.That(raid.Decide(raid['P'], raid['b']).IsReject(), Is.True);
        }

        [Test]
        public void UnreachableWorkCellsAreNotJudged()
        {
            var map = new GridMap(
                "E....###..",
                ".....#A#..",
                "hhh..###..",
                "hPD.......",
                "hhh.......");
            Assert.That(map.Decide(map['P'], map['A']), Is.EqualTo(Verdict.NoWorkCell));
        }

        [TestCase(10, 10, true)]
        [TestCase(18, 10, true)]
        [TestCase(19, 10, false)]
        [TestCase(125, 100, true)]
        [TestCase(126, 100, false)]
        [TestCase(ThreatGeometry.Unreached, 10, false)]
        public void DetourTolerance(int safe, int direct, bool realistic)
            => Assert.That(ThreatGeometry.SafeRouteIsRealistic(safe, direct), Is.EqualTo(realistic));

        [Test]
        public void LargeMapRebuildIsLinear()
        {
            const int size = 250;
            var rows = new string[size];
            for (int z = 0; z < size; z++) rows[z] = new string('.', size);
            char[] middle = rows[size / 2].ToCharArray();
            for (int x = 100; x < 150; x++) middle[x] = 'H';
            middle[120] = 'P';
            middle[10] = 'E';
            middle[240] = 'A';
            rows[size / 2] = new string(middle);
            var map = new GridMap(rows);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) map.Rebuild();
            Verdict v = map.Decide(map['P'], map['A']);
            watch.Stop();
            Assert.That(v, Is.EqualTo(Verdict.ExteriorSafe));
            TestContext.WriteLine("20 snapshot rebuilds of a 250x250 map + 1 decision: "
                + watch.Elapsed.TotalMilliseconds.ToString("F1") + " ms (headless Mono/.NET, not in-game FPS)");
        }
    }
}
