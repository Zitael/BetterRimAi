# BetterRimAI

A RimWorld 1.6 mod focused on smarter pawn work planning.

## v0.1 — long-trip need guard

When vanilla RimWorld selects a normal work job more than **50 cells** away, BetterRimAI checks the pawn before allowing the trip.

Current preparation thresholds:

- Food: 38%
- Rest: 30%

If one of those needs is low, the mod asks RimWorld's own vanilla `JobGiver_GetFood` / `JobGiver_GetRest` to produce the appropriate job and uses it instead of the distant work. If vanilla cannot produce that job yet, the distant work is deferred rather than sending the pawn across the map immediately before a need interruption.

Emergency work and player-forced jobs are not changed.

This is deliberately a small first iteration. The next step is work batching: once a pawn has paid the travel cost to reach a remote work area, prefer nearby compatible jobs before returning to global job selection.

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
[BetterRimAI] v0.1 loaded: long-trip need guard enabled.
```

When the guard activates, it logs a line such as:

```text
[BetterRimAI] Bob: distant work 87 cells, food=34%, rest=62% -> replaced distant work with food.
```

## Development workflow

Feature work goes to branches and pull requests. `main` is kept as the stable/tested version.
