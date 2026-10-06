# Threat safety

> Let vanilla RimWorld choose jobs normally. Interfere only when BetterRimAI has a clear reason.

## Model

```
MapThreatState (per map, refreshed lazily every 120 ticks)
  no active threat ──────────────────────────────► every hook returns: NO-OP
  active threat
    pawn eligible? (player, not animal, not drafted, not Attack response, not forced)
      no ────────────────────────────────────────► vanilla
    pawn standing in protected space?
      no (already outside) ──────────────────────► vanilla
    JobGiver_Work candidate: from which cells would vanilla do this work?
      some cell inside the pawn's protected area ─► allowed (SAFE_SIDE path if the outside side is unsafe)
      all cells elsewhere, cell + realistic route clear ► allowed
      all cells elsewhere, danger at cell or on route ► candidate rejected; vanilla scans on
```

The decision is made **before** a job exists, inside vanilla's own candidate scan, so a
rejected candidate behaves exactly like one vanilla considered invalid: the scanner moves to
the next target (often the same kind of work indoors) or the next WorkGiver.

## Harmony surface

| Patch | Target | Why | Runs with no threat? |
|---|---|---|---|
| `WorkScanCallSitePatch` (transpiler) | JobGiver_Work's `Validator` / `ProcessCell` local functions (found by IL scan; "Prioritize" orders excluded) | Redirects the `HasJobOnThing` / `HasJobOnCell` calls to `WorkScanFilter`. Covers every vanilla and modded scanner (Pick Up And Haul included) with ~2 patched methods. | The redirect runs, but with no restriction it is a direct call to the scanner with identical arguments. |
| `WorkScanSelectionPatch` (prefix/postfix/finalizer) | `JobGiver_Work.TryIssueJobPackage` | Opens the per-pass restriction only when a threat is active and the pawn is protected; validates the completed job's secondary targets (haul destination, delivery blueprint, PUAH queue) and rescans without an unsafe one. | Prefix: eligibility + map flag. Postfix: one null check. |
| `SafeSidePathPatch` (prefix) | `Pawn_PathFollower.StartPath` | Safe-side Touch work: rewrites Touch → a protected cell that passes vanilla's `ReachabilityImmediate.CanReachImmediate` and reachability, only when the outside side is unsafe. | Exits on the map flag. Never changes a path without an active threat. |
| `ThreatSafetyJobTracePatch` (prefix) | `Pawn_JobTracker.StartJob` | Diagnostics only (selected-pawn trace). | Exits on the debug setting. |

Unrelated features: `LongTripNeedsPatch` (JobGiver_Work postfix), remote-work locality
(`StartJob`, `EndCurrentJob`, `WorkGiversInOrderNormal`, JobGiver_Work prefix; its candidate
filter now shares `WorkScanFilter` instead of patching every scanner override).

Removed: per-scanner `HasJobOnThing` / `HasJobOnCell` / `JobOnThing` / `JobOnCell` /
`NonScanJob` patches, the universal `ThinkNode_JobGiver` postfix, the
`TryEnterNextPathCell` path guard, deferred cancellation on `JobTrackerTick(Interval)`, the
Pick Up And Haul `HasJobOnThing` patch, and the `GetInspectString` UI hook.

## Threats

- Source: vanilla `AttackTargetsCache.TargetsHostileToColony`, filtered with
  `GenHostility.IsActiveThreatTo(t, Faction.OfPlayer, ignoreHives: false, canBeFogged: false)`.
  This excludes downed, dormant and threat-disabled entities and anything sealed in fog
  (an unopened Ancient Complex). Hive defenders count because they attack pawns that approach.
- Hostile pawns: danger spreads by a multi-source walking BFS up to the configured radius;
  walls and closed doors stop it. A hostile behind the closed doors of an opened complex
  still does not project danger outside.
- Hostile turrets (any non-pawn attacker with a ranged verb): the cells the verb can hit from
  its position (`Verb.CanHitTargetFrom`: range, minimum range, line of fire), capped at 45 cells,
  cached per turret for 1250 ticks or until it moves.

## Protected → outside

- Protected space: painted Home plus unpainted pockets enclosed by Home (flood from the map edge).
- Components: 4-connected protected walkable cells (vanilla forbids corner cutting, so this
  matches walkability).
- Work cells: `OnCell` → the target cell; `InteractionCell` → the interaction cell;
  `ClosestTouch` on a standable single cell → that cell; otherwise every walkable cell around
  the target from which `ReachabilityImmediate.CanReachImmediate(..., Touch)` holds. This is
  vanilla's own rule, including its diagonal-corner restrictions.
- A work cell in the pawn's own component means no departure. Any other cell (open exterior,
  or a separate patch of Home such as the auto-home ring around an outer wall) means walking
  out, and is judged with two distance fields from the pawn's component
  (shortest walk; shortest danger-free walk): unsafe if the cell is in danger, has no
  danger-free route, or the danger-free route is longer than direct + max(8, 25%).

## Performance

- No threat: per JobGiver_Work pass, an eligibility check and a map-flag read; every 120 ticks
  per map, one walk over vanilla's hostile-target set (usually empty). No grids exist.
- Active threat, every 120 ticks per map: envelope flood, walkable/door scan, component
  labelling, danger BFS (all O(map cells), ~2 ms for 250×250 in headless Mono), turret lanes
  from cache. Distance fields: two BFS per protected component, lazily, at most once per refresh.
- Candidate check: interior targets exit after a (w+2)(h+2) protected lookup; boundary
  targets add at most (w+2)(h+2) vanilla touch checks and O(1) grid lookups. No pathfinding,
  hostile scans, LINQ or reflection per candidate.
- State is one `ConditionalWeakTable<Map, …>` entry per map; nothing per pawn.

## Known limits

- Selection-time decision only: a pawn that set out while it was safe is not stopped if a
  threat appears on its way (vanilla's flee response handles that).
- A target in protected space is not checked for a vanilla route that briefly crosses outside.
- Jobs not produced by JobGiver_Work (Pick Up And Haul's in-job extra pickups, opportunistic
  hauling, mod think nodes) are not filtered.
- Danger is walking distance, not weapon simulation; long-range raiders are approximated by
  the radius.

## Manual playtest checklist

Enable **Debug threat decisions** and select the pawn under test; each started job logs
`BetterRimAI action=...`.

- **A. Peaceful roof.** No threat. Paint Build Roof inside Home, along its edge, on a corner
  over a wall, and across several cells. Expect: all built, no standing still, no
  "started 10 jobs in one tick", every trace line `threatState=CLEAR ... action=NONE`.
- **B. Peaceful construction/repair.** No threat. Build and repair inside, on the Home
  boundary and outside. Expect: vanilla behavior, `action=NONE`.
- **C. Raid, pawn inside.** Raiders outside; the pawn has indoor and outdoor work. Expect:
  `THREAT STATE became ACTIVE`, outdoor candidates `action=REJECT`, the pawn does indoor
  work/needs/joy and never walks out.
- **D. Exterior-only repair.** Damaged hull/wall whose only touch cells are outside (block
  the inside cell, e.g. with an engine); hostiles near it. Expect: Repair is never started,
  no walking back and forth.
- **E. Safe-side repair.** Same wall with a free inside cell. Expect: `action=SAFE_SIDE`,
  repaired from inside.
- **F. Pawn outside when the threat appears.** Expect: no BetterRimAI lines for that pawn
  except `action=NONE`; vanilla flees/returns; no stop/start loop.
- **G. Closed Ancient Complex.** Dormant/sealed mechs nearby. Expect: state stays `CLEAR`
  (or the complex area is not dangerous), outdoor work near it continues.
- **H. Complex opened.** Wake/release the mechs. Expect: `ACTIVE` once they can reach
  open ground; work near them is rejected.
- **I. Turret behind a wall.** Hostile turret whose line of fire is blocked. Expect: work
  behind the obstruction continues.
- **J. Turret with a firing lane.** Expect: work in the lane is rejected.
- **K. Threat ends.** Kill/remove all threats. Expect: `THREAT STATE became CLEAR` within
  about 2 seconds and outdoor work resumes.
- **L. Overrides.** Draft, give a direct order, use "Prioritize", set Attack response.
  Expect: the pawn goes outside as ordered.
