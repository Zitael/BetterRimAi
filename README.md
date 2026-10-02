# BetterRimAI

A RimWorld 1.6 mod focused on smarter pawn work planning.

## Long-trip need guard

When vanilla RimWorld selects a normal work job more than **50 cells** away, BetterRimAI checks the pawn before allowing the trip.

Current preparation thresholds:

- Food: 45%
- Rest: 40%

If one of those needs is low, the mod asks RimWorld's own vanilla `JobGiver_GetFood` / `JobGiver_GetRest` to produce the appropriate job and uses it instead of the distant work. If vanilla cannot produce that job yet, the distant work is deferred rather than sending the pawn across the map immediately before a need interruption.

Emergency work and player-forced jobs are not changed.

## Threat-aware outdoor work

Automatic work whose destination is outside the player's **Home area** is checked against the pawn's actual vanilla path.

By default:

- the feature is enabled;
- a hostile within **15 cells** of the calculated route blocks the automatic outdoor job;
- a hostile within **20 cells** of the route's Home-area exit blocks the job before the pawn leaves the base;
- hostiles elsewhere on the map do not matter;
- drafted pawns and colonists whose hostility response is **Attack** bypass this restriction.

The hostile check covers hostile pawns such as raiders, manhunters and shamblers through RimWorld's normal `HostileTo` relationship.

The two radii and the feature toggle are available under **Options → Mod settings → Better Rim AI**.

This version blocks an unsafe trip rather than rewriting RimWorld 1.6's low-level pathfinder. That deliberately keeps the mod lightweight and compatible while still preventing a pawn from opening the base and walking through a hostile corridor.

Pick Up And Haul is supported optionally through runtime Harmony/reflection compatibility. BetterRimAI does not take a compile-time dependency on Pick Up And Haul and must continue to load when that mod is absent.

## Requirements

- RimWorld 1.6
- .NET SDK (8.x is fine for building; the assembly itself targets .NET Framework 4.7.2)
- Harmony for RimWorld (Steam Workshop item `2009463077`)

Do **not** copy `0Harmony.dll` into this mod. Harmony is a shared RimWorld dependency.

## Build on Windows

Clone the repository and run this from its root:

```powershell
dotnet build .\Source\BetterRimAI.csproj -c Release -p:RimWorldDir="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
```

If RimWorld is in another Steam library, replace the path after `RimWorldDir=`.

The build automatically creates a ready-to-install mod here:

```text
dist\BetterRimAI\
├── About\
│   └── About.xml
└── Assemblies\
    ├── BetterRimAI.dll
    └── BetterRimAI.pdb
```

## Regression tests

The `Tests` project contains NUnit regression tests for compatibility bugs that have already occurred in development, including:

- Harmony prefixes must not depend on parameter names chosen by another mod (`t` vs `thing`, etc.);
- Pick Up And Haul support must remain optional with no compile-time assembly dependency;
- a danger block identified by a `thingIDNumber` must still match the lightweight probe job used while scanning candidates;
- a different Thing must not be accidentally blacklisted;
- cell-based blocks must still match by job type plus destination.

Run the suite from the repository root:

```powershell
dotnet test .\Tests\BetterRimAI.Tests.csproj -c Release `
  -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld"
```

`RimWorldDir` is required because the production assembly and a few regression tests compile against RimWorld's `Assembly-CSharp.dll`.

Before merging behavior changes, both `dotnet build` and `dotnet test` should pass.

## Install locally

Copy the entire generated `dist\BetterRimAI` directory to:

```text
<RimWorld>\Mods\BetterRimAI
```

For the default Steam install that becomes:

```text
C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\BetterRimAI
```

Then start RimWorld, open **Mods**, enable **Harmony** and **Better Rim AI**, keep Harmony above Better Rim AI, and restart when RimWorld asks.

On startup the log should contain:

```text
[BetterRimAI] loaded: long-trip need guard + threat-aware outdoor work enabled.
```

When the long-trip guard activates, it logs a line such as:

```text
[BetterRimAI] Bob: distant work 87 cells, food=34%, rest=62% -> replaced distant work with food.
```

When threat-aware outdoor work blocks a trip, it logs a throttled line identifying whether the threat was near the Home-area exit or the calculated route.

## Development workflow

Feature work goes to branches and pull requests. `main` is kept as the stable/tested version.

## Outdoor candidate selection and performance

The candidate gate first consults a **60-tick map danger snapshot**. Hostile sources
create exterior risk zones; one standability flood from protected Home cells identifies
outdoor cells reachable without crossing those zones. Subsequent scanner candidates need
one indexed lookup. This rejects known unsafe outdoor work before travel. The existing
actual-path guard remains a last resort if vanilla chooses a different route or danger
changes after selection. It now checks exterior path cells even when the target itself
is inside Home (for example, an outer wall approached from outdoors).

- `HasJobOnThing` / `HasJobOnCell` reject exterior candidates when the danger snapshot
  says a safe route is unavailable or while that pawn's observed-route restriction is
  active. Vanilla scanners continue looking for another target, including
  indoor targets of the **same JobDef**. Home cells and enclosed unpainted Home pockets
  use the existing `ThreatAwareHomeSafety` definition.
- `JobOnThing` / `JobOnCell` also guard direct construction and inspect completed jobs'
  secondary targets and target queues. `NonScanJob` returning null lets work selection
  continue to another giver. The ThinkNode fallback covers `ThinkNode_JobGiver` and the
  separate `JobGiver_Work` hierarchy; returning `NoJob` lets parent nodes try alternatives.
- Drafted, explicit player-forced and configurable Attack-response pawns bypass the gate.
  The deferred cancellation callback rechecks these overrides, including queued forced jobs.
- Route evidence is revalidated on demand every **120 ticks** and remains active only
  while danger is still near the recorded route/exit cell. It does not blindly reopen a
  retry window every 600 ticks. Dead/downed/despawned hostiles and disabled turrets cannot renew it. Outdoor
  work becomes available at the next expired selection check after danger disappears;
  hostility changes can additionally encounter the existing **60-tick hostile cache**.
  No selected jobs means there is no polling or background scan just to clear a cache.
- A known-target dictionary uses map + Thing identity, or map + JobDef + cell for cell
  targets. It never bans an entire JobDef. Expired evidence is removed when revalidated.
  Pawn restrictions use weak object keys plus map identity; all runtime evidence and Home
  caches reset on a fresh game/load to prevent reused IDs or tick rollback stranding pawns.

### Cost boundaries

The warm Thing/Cell scanner gate is O(1)-like: eligibility checks, cached pawn evidence,
protected-cell lookup and an indexed reachability/target lookup. It allocates no probe Job or Harmony
`object[]` argument array and performs no reflection, LINQ, pathfinding or hostile scan.
Reflection/type discovery is startup-only; Harmony uses positional `__0`/`__1`/`__2`
bindings, including optional Pick Up And Haul support, to tolerate foreign parameter names.
`StartPath` cache invalidation now uses direct field access.

Exceptional work is bounded by events/cache expiry: the first outdoor candidate on a map
and subsequent 60-tick refreshes rebuild one map danger grid and one standability flood.
Hostile snapshots are throttled to 60 ticks. The existing Home flood-fill runs on cache
creation/1200-tick expiry. Selected exterior construction jobs may use vanilla reachability
to verify a protected interaction cell; the scanner's per-target gate does not pathfind.
Completed-job queue validation is O(queue length), outside individual scanner-candidate
checks. The movement safety net samples the actual route/threats on a new path, six
traversed cells, or 120-tick recheck. No `PatherTick` patch has been added.

Mobile threats use vanilla hostility in either pawn direction or against its player faction and native
potential-threat state (where the full map runtime is available). This covers hostile
mechanoids, human/modded-faction pawns, insects, shamblers and aggressive animals; dead,
downed, dormant/deactivated or otherwise inactive entities are excluded. The same
60-tick snapshot also reads registered hostile combat structures from
`AttackTargetsCache`, without scanning every building. Turrets use their actual attack
verb and effective weapon range rather than the mobile-threat proximity radius. Native
shooting checks honor minimum range and obstructions. Every exterior route cell is
checked for turret fire, preserving narrow firing lanes between mobile-threat samples.
Lazy firing geometry is shared per source/map/verb/origin and expires after 120 ticks;
it costs one byte per map cell per checked firing source. Only route validation or
expired evidence can populate it. Warm candidate selection never calls a shooting verb.
Power-off, despawn and changed hostility are reflected when expired evidence rebuilds
the threat snapshot. Home safety semantics and all player overrides remain unchanged.

For `Touch` jobs, `StartPath` prefers a reachable protected work cell next to the original
target. This keeps the vanilla job target and its reservation while letting an outer wall,
ship part, blueprint or frame be repaired/constructed from inside when possible. An
exterior construction candidate with a protected work side stays eligible under threat;
an inaccessible safe side is rejected at completed-job validation. Direct player orders
and drafted or Attack-response pawns bypass this choice.

With **Prefer nearby work at remote sites** enabled, after a successful work trip of at
least 50 cells from Home, the same WorkGiver gets a
temporary local preference within 24 cells of the remote site. WorkGivers of strictly
higher priority remain ahead; if the preferred giver has no valid nearby work, vanilla
continues down its normal list. The preference expires after 2500 ticks or leaving the
site and does not apply to emergency work, forced orders or critical Food/Rest below 20%.
Vanilla candidate validity, reservations and reachability remain in charge.

The preliminary map grid conservatively uses weapon-range envelopes for turrets; the
path guard uses actual verb line of fire. This avoids thousands of shooting checks per
snapshot but can temporarily defer an outdoor job behind cover until no threat remains.

With debug logging enabled, per-pawn/per-stage logs are limited to once per 600 ticks:
`candidate-rejected-before-movement`, `active-path-cancelled-safety-net`, and
`restriction-cleared`. Optional PUAH rejection and haul-definition diagnostics remain.
Log throttling happens before formatting target/job details.

### Validation and manual playtest

The automated suite exercises actual candidate/fallback methods with small headless
RimWorld fixtures, real Harmony positional-prefix installation, installed game method
signatures, cache expiry/renewal, Home pockets, overrides, queued haul targets and load/map
changes. The warm-loop test checks 100,000 candidates with no accessible hostile roster
or pathfinder. It is a regression test, **not a measurement of in-game FPS**. The installed
mod list and full Unity simulation still need the following short playtest:

1. Save a colony with indoor hauling/cleaning/crafting available and exterior hauling or
   cell work beyond a raider-guarded exit. Use undrafted Flee/Ignore pawns. Verify that
   outdoor candidates are rejected before travel and indoor work is selected without
   start/cancel oscillation. Check both Thing and Cell work and an enclosed Home-paint hole.
2. Use a forced outdoor order, draft the pawn, and separately set Attack response. Verify
   each can leave; return to autonomous Flee/Ignore and verify protection resumes.
3. Remove/kill/down all nearby threats or move them away from the dangerous route. Verify
   outdoor work resumes after the expiry/next job-selection check, also after save/reload.
4. Repeat with Pick Up And Haul enabled and absent. Include an indoor primary pickup with
   an outdoor queued pickup, and needs/recreation fallback. Check the three diagnostic
   stages and ensure there are no Harmony errors at startup.
5. Repeat with insects, shamblers and manhunters. Put an enemy turret more than the
   configured pawn-threat radius away, with an outdoor route inside its weapon range.
   Include a narrow firing lane farther along the route. Verify autonomous pawns stay
   safe, walls block turret fire, and removing the turret or its power restores outdoor
   work after revalidation. Compare enabled/disabled using this turret save too.
6. Repeat the raid with hostile mechanoids and a modded hostile faction/cultist. Check
   dead, downed, dormant and disabled enemies no longer hold the restriction after expiry.
7. Damage an exterior wall or ship part and place a blueprint/roof/pipe near the Home
   boundary. Verify the pawn works from a reachable inside cell and still reserves the
   original target. If no safe side exists, verify it chooses other safe work during a raid.
8. Send a pawn to mine or construct more than 50 cells from Home. After a completed job,
   verify another nearby job of the same giver is preferred over lower-priority base work.
   Raise Food or Rest below 20% and verify the pawn can return; repeat for urgent medical
   work and a direct forced order.

### Same-save enabled/disabled FPS comparison

Use the in-game **Threat-aware outdoor work** checkbox; leave the mod and mod list installed
for both runs. Disable **Debug threat decisions** for timing. Keep the same resolution,
camera, zoom, game speed, pawn count and FPS/TPS overlay/profiler for every run.
Keep **Prefer nearby work at remote sites** at the same setting in both raid runs. For a
remote-mine comparison, toggle that setting instead while holding threat avoidance fixed.

Load the same paused raid save, enable the feature, allow about 15 seconds of warm-up,
then record FPS/frame time and TPS over 60 seconds. Reload that exact save, disable the
feature, and repeat the same interval. Run at least three pairs, alternating order, and
compare medians and frame-time spikes. Repeat with a quiet save to measure the idle gate
cost. Reload between arms because enabled/disabled pawn decisions change subsequent
simulation work. FPS can be capped, so TPS and frame time help expose CPU regressions.

To build/test without deploying into your installed game, override the existing install
output path (the normal build still stages `dist/BetterRimAI`):

```powershell
dotnet build .\Source\BetterRimAI.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld" -p:ModInstallDir="$PWD\dist\test-install"
dotnet test .\Tests\BetterRimAI.Tests.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld" -p:ModInstallDir="$PWD\dist\test-install"
```
