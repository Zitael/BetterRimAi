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
- drafted pawns, player-forced jobs and colonists whose hostility response is **Attack** bypass this restriction.

The hostile check covers hostile pawns such as raiders, manhunters and shamblers through RimWorld's normal `HostileTo` relationship.

The two radii and the feature toggle are available under **Options → Mod settings → Better Rim AI**.

This version blocks an unsafe automatic trip rather than rewriting RimWorld 1.6's low-level pathfinder. That deliberately keeps the mod lightweight and compatible while still preventing a pawn from opening the base and walking through a hostile corridor.

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
