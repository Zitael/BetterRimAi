# BetterRimAI

A RimWorld 1.6 mod focused on smarter pawn work planning.

## Long-trip need guard

When vanilla RimWorld selects a normal work job more than **50 cells** away, BetterRimAI checks the pawn before allowing the trip.

Current preparation thresholds:

- Food: 45%
- Rest: 40%

If one of those needs is low, the mod asks RimWorld's own vanilla `JobGiver_GetFood` / `JobGiver_GetRest` to produce the appropriate job and uses it instead of the distant work. If vanilla cannot produce that job yet, the distant work is deferred rather than sending the pawn across the map immediately before a need interruption.

Emergency work and player-forced jobs are not changed.

## Threat safety: don't casually leave the base during a threat

While an active hostile threat makes the outside unsafe, a colonist or player mech that is
**inside the protected colony** does not autonomously pick work that would require stepping
into that danger (hauling, mining, construction, repair, roofing, cleaning, firefighting...).
Vanilla stays free to choose any other work, needs, joy or rest that can be done safely.

- **No threat: no influence.** Every hook exits on a cheap per-map "no active threat" check.
  Nothing is rejected, rerouted or cancelled, and no extra think cycles happen.
- **Pawns already outside are never touched.** Fleeing, returning, needs and work stay vanilla.
- **Drafted pawns, direct/forced orders (including "Prioritize") and Attack-response
  colonists bypass it.** Animals are not affected.
- **Safe-side work:** an outer wall or hull that can be touched from a protected cell is
  repaired/built from inside, verified with vanilla's own touch rule.
- **Nothing is stopped mid-path.** Unsafe work is simply never selected; there is no path
  guard and no job cancellation.

Definitions:

- *Protected colony* = painted Home area plus unpainted pockets fully enclosed by Home.
- *Active threat* = vanilla `GenHostility.IsActiveThreatTo` for the player (hostile, able to
  attack, not downed/dormant, not sealed away in fog), hive defenders included.
- *Danger* = cells within the configured walking distance (default 18) of a hostile pawn,
  spreading only through passable cells (walls and closed doors stop it), plus the cells a
  hostile turret can actually hit (`Verb.CanHitTargetFrom`: range, minimum range, cover).
- *Requires unsafe departure* = every cell vanilla could do the work from is outside, and
  either that cell is in danger, or there is no danger-free route to it, or the danger-free
  route is a detour vanilla's shortest path would not take.

Debug: enable **Debug threat decisions** in the mod settings. The log then shows threat
state changes (`THREAT STATE became ACTIVE/CLEAR`), rejected candidates (`action=REJECT`),
safe-side paths (`action=SAFE_SIDE`) and, for the **selected pawn**, one line per started
job with `BetterRimAI action=NONE` or what was changed. Any change made while no threat is
active is logged as an `INVARIANT VIOLATION` error regardless of the setting.

See [docs/threat-safety.md](docs/threat-safety.md) for the architecture, Harmony surface,
performance notes and the manual playtest checklist.

## Remote-work locality

With **Prefer nearby work at remote sites** enabled, after a successful work trip of at
least 50 cells from the protected colony, the same WorkGiver gets a temporary preference
within 24 cells of the remote site. Strictly higher-priority work stays ahead; the
preference expires after 2500 ticks, when leaving the site, and never applies to emergency
work, forced orders or Food/Rest below 20%.

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

## Tests

- `Tests/` - pure tests of the threat geometry and decision rules (protected envelope,
  components, danger spread through walls/doors, safe-route/detour rule, safe-side and
  roof-corner decisions). They use no RimWorld types at runtime.
- `Tests.RimWorld/` - integration tests that need the installed game's real
  `Assembly-CSharp.dll`: the Harmony surface, the JobGiver_Work call-site redirection, the
  no-threat pass-through and remote-work locality.

```powershell
dotnet test .\Tests\BetterRimAI.Tests.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld"
dotnet test .\Tests.RimWorld\BetterRimAI.RimWorldTests.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld"
```

Neither project simulates a running colony; the job lifecycle (job drivers, pathing,
think tree) is covered by the manual checklist in `docs/threat-safety.md`. `Tests.RimWorld`
runs on .NET Framework, which cannot load some RimWorld types that Unity's Mono accepts; the
active-threat snapshot is reported as inconclusive there when that happens and is validated
in game.

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
[BetterRimAI] loaded: long-trip need guard, threat safety and remote-work locality enabled.
```

When the long-trip guard activates, it logs a line such as:

```text
[BetterRimAI] Bob: distant work 87 cells, food=34%, rest=62% -> replaced distant work with food.
```


## Development workflow

Feature work goes to branches and pull requests. `main` is kept as the stable/tested version.

To build/test without deploying into your installed game, override the existing install
output path (the normal build still stages `dist/BetterRimAI`):

```powershell
dotnet build .\Source\BetterRimAI.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld" -p:ModInstallDir="$PWD\dist\test-install"
dotnet test .\Tests\BetterRimAI.Tests.csproj -c Release -p:RimWorldDir="D:\Progs\Steam\steamapps\common\RimWorld" -p:ModInstallDir="$PWD\dist\test-install"
```
