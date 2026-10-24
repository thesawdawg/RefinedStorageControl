param(
    [string]$GameRoot,
    [string]$Configuration = 'Release',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot

if (-not $GameRoot) {
    $GameRoot = (Resolve-Path -LiteralPath (Join-Path $projectRoot '..\..')).Path
}
$managed = Join-Path $GameRoot '7DaysToDie_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "Game managed assemblies not found at $managed"
}

# Fail fast on malformed mod XML before any build work.
$configDir = Join-Path $projectRoot 'Mod\Config'
if (Test-Path -LiteralPath $configDir) {
    Get-ChildItem -LiteralPath $configDir -Filter '*.xml' -File -Recurse | ForEach-Object {
        try { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null }
        catch { throw "Malformed XML in $($_.FullName): $($_.Exception.Message)" }
    }
}

& dotnet build (Join-Path $projectRoot 'Source\RefinedStorageControl.csproj') -c $Configuration --nologo "-p:GameManaged=$managed"
if ($LASTEXITCODE -ne 0) { throw 'Source build failed.' }

# The test build intentionally receives no game references: it is the purity
# boundary check for the Core/Storage/Access/Routing modules.
& dotnet build (Join-Path $projectRoot 'Tests\RefinedStorageControl.Tests.csproj') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests build failed.' }

& (Join-Path $projectRoot "Tests\bin\$Configuration\net48\RefinedStorageControl.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$artifact = Join-Path $projectRoot 'Artifacts\RefinedStorageControl'
if (Test-Path -LiteralPath $artifact) { Remove-Item -LiteralPath $artifact -Recurse -Force }
New-Item -ItemType Directory -Path $artifact | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'Mod\*') -Destination $artifact -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot "Source\bin\$Configuration\net48\RefinedStorageControl.dll") -Destination $artifact

# Lib.Harmony dependency (required by the wire-policy patches).
$harmonyDll = Join-Path $projectRoot "Source\bin\$Configuration\net48\0Harmony.dll"
if (Test-Path -LiteralPath $harmonyDll) {
    Copy-Item -LiteralPath $harmonyDll -Destination $artifact
} else {
    throw "0Harmony.dll not found in build output at $harmonyDll"
}
$harmonyLicense = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\lib.harmony') -Recurse -Filter 'LICENSE' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($harmonyLicense) {
    Copy-Item -LiteralPath $harmonyLicense.FullName -Destination (Join-Path $artifact 'Harmony-LICENSE.txt')
}

$managedNames = @{}
Get-ChildItem -LiteralPath $managed -File | ForEach-Object { $managedNames[$_.Name.ToLowerInvariant()] = $true }
$offenders = Get-ChildItem -LiteralPath $artifact -Recurse -File |
    Where-Object { $managedNames.ContainsKey($_.Name.ToLowerInvariant()) }
if ($offenders) {
    throw 'Artifact must not contain game assemblies: ' + (($offenders | ForEach-Object Name) -join ', ')
}

"Artifact: $artifact"

if ($Install) {
    $installDir = Join-Path $GameRoot 'Mods\RefinedStorageControl_Dev'
    if ([IO.Path]::GetFullPath($installDir).TrimEnd('\') -ieq [IO.Path]::GetFullPath($projectRoot).TrimEnd('\')) {
        throw "Install target would overwrite the dev repo: $installDir"
    }
    if (Test-Path -LiteralPath $installDir -PathType Container) {
        $existing = @(Get-ChildItem -LiteralPath $installDir -Recurse -File)
        if ($existing.Count -gt 0) {
            $manifest = Join-Path $installDir 'ModInfo.xml'
            if (-not (Test-Path -LiteralPath $manifest) -or
                -not (Select-String -LiteralPath $manifest -Pattern 'Name value="RefinedStorageControl"' -Quiet)) {
                throw "Install target contains files that are not this mod: $installDir"
            }
        }
    }
    else {
        New-Item -ItemType Directory -Path $installDir | Out-Null
    }
    try {
        Copy-Item -Path (Join-Path $artifact '*') -Destination $installDir -Recurse -Force
        $wanted = @{}
        Get-ChildItem -LiteralPath $artifact -Recurse -File | ForEach-Object {
            $wanted[$_.FullName.Substring($artifact.Length).TrimStart('\').ToLowerInvariant()] = $true
        }
        Get-ChildItem -LiteralPath $installDir -Recurse -File |
            Where-Object { -not $wanted.ContainsKey($_.FullName.Substring($installDir.Length).TrimStart('\').ToLowerInvariant()) } |
            Remove-Item -Force
    }
    catch {
        throw "Install failed: $($_.Exception.Message). Close the game first; RefinedStorageControl.dll stays locked while it is running."
    }
    "Installed: $installDir"
}
