param(
    [string]$RimWorldDir
)

$ErrorActionPreference = "Stop"

$Project = Join-Path $PSScriptRoot "Source\BetterRimAI.csproj"
$Dist = Join-Path $PSScriptRoot "dist\BetterRimAI"

function Test-RimWorldDir {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $assembly = Join-Path $Path "RimWorldWin64_Data\Managed\Assembly-CSharp.dll"
    return (Test-Path $assembly)
}

function Find-RimWorldDir {
    $candidates = @(
        "$env:ProgramFiles(x86)\Steam\steamapps\common\RimWorld",
        "$env:ProgramFiles\Steam\steamapps\common\RimWorld"
    )

    foreach ($drive in Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue) {
        $candidates += (Join-Path $drive.Root "SteamLibrary\steamapps\common\RimWorld")
        $candidates += (Join-Path $drive.Root "Steam\steamapps\common\RimWorld")
        $candidates += (Join-Path $drive.Root "Progs\Steam\steamapps\common\RimWorld")
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-RimWorldDir $candidate) {
            return $candidate
        }
    }

    return $null
}

if ($RimWorldDir) {
    $RimWorldDir = [System.IO.Path]::GetFullPath($RimWorldDir)

    if (-not (Test-RimWorldDir $RimWorldDir)) {
        throw "Invalid RimWorld directory: '$RimWorldDir'. Expected RimWorldWin64_Data\Managed\Assembly-CSharp.dll."
    }
}
else {
    Write-Host "Searching for RimWorld..." -ForegroundColor DarkGray
    $RimWorldDir = Find-RimWorldDir

    if (-not $RimWorldDir) {
        throw "Could not find RimWorld automatically. Run: .\deploy.ps1 -RimWorldDir '<path-to-RimWorld>'"
    }
}

$Target = Join-Path $RimWorldDir "Mods\BetterRimAI"

Write-Host ""
Write-Host "BetterRimAI deploy" -ForegroundColor Cyan
Write-Host "RimWorld: $RimWorldDir"
Write-Host "Target:   $Target"

Write-Host ""
Write-Host "Building..." -ForegroundColor Yellow

& dotnet build $Project -c Release "-p:RimWorldDir=$RimWorldDir"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $Dist)) {
    throw "Build succeeded, but dist directory was not found: $Dist"
}

Write-Host ""
Write-Host "Installing..." -ForegroundColor Yellow

if (Test-Path $Target) {
    Remove-Item $Target -Recurse -Force
}

Copy-Item $Dist $Target -Recurse -Force

$dll = Join-Path $Target "Assemblies\BetterRimAI.dll"
if (-not (Test-Path $dll)) {
    throw "Deployment failed: BetterRimAI.dll was not found at $dll"
}

Write-Host ""
Write-Host "BetterRimAI deployed successfully!" -ForegroundColor Green
Write-Host "DLL: $dll"
Write-Host "Restart RimWorld to load the new version." -ForegroundColor Cyan
