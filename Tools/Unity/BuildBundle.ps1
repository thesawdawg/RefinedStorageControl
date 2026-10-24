param(
    # Must match the game's Unity minor release (3.3 b18 ships 2022.3.62f2).
    [string]$UnityExe,
    [switch]$SkipBlender
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)

if (-not $UnityExe) {
    $UnityExe = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\2022.3.62*\Editor\Unity.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $UnityExe -or -not (Test-Path -LiteralPath $UnityExe)) {
    throw 'Unity 2022.3.62 not found; pass -UnityExe <path to Unity.exe>.'
}

# Models first: the bundle is built from Art/Models/*.fbx.
if (-not $SkipBlender) {
    $blender = Get-ChildItem 'C:\Program Files\Blender Foundation\Blender*\blender.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $blender) { throw 'Blender not found; run with -SkipBlender to reuse Art/Models.' }
    & $blender -b --factory-startup -P (Join-Path $repo 'Tools\Art\rsc_art.py') -- $repo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Blender export failed.' }
}

$project = Join-Path $PSScriptRoot 'RscBundle'
$log = Join-Path $project 'Logs\bundle-build.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$p = Start-Process -FilePath $UnityExe -Wait -PassThru -NoNewWindow -ArgumentList @(
    '-batchmode', '-quit', '-nographics',
    '-projectPath', "`"$project`"",
    '-executeMethod', 'RscBundleBuilder.Build',
    '-rscRoot', "`"$repo`"",
    '-logFile', "`"$log`"")
Select-String -LiteralPath $log -Pattern '\[RSC\]|error CS' | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) { throw "Unity bundle build failed (exit $($p.ExitCode)); see $log" }
"Bundle: " + (Join-Path $repo 'Mod\Resources\rsc.unity3d')
