[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot "ExteraMonitor.csproj") -Raw
    $Version = $project.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "Version was not found in ExteraMonitor.csproj." }
}
$artifactsDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $projectRoot "artifacts"
} else {
    [IO.Path]::GetFullPath($OutputDirectory)
}
$stageDir = Join-Path $artifactsDir "stage\ExteraMonitor"
$portableName = "ExteraMonitor-v$Version-win-x64-portable"
$portableDir = Join-Path $artifactsDir $portableName
$portableZip = Join-Path $artifactsDir "$portableName.zip"
$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) { throw "Inno Setup 6 compiler was not found." }

$requiredRuntime = @(
    (Join-Path $projectRoot "Driver\ExteraMonitorDriver.sys"),
    (Join-Path $projectRoot "Driver\kvc.exe"),
    (Join-Path $projectRoot "Driver\kvc.dat")
)
foreach ($required in $requiredRuntime) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required release input is missing: $required" }
}

foreach ($required in @($innoCompiler)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required release input is missing: $required" }
}

foreach ($oldArtifact in @(
    $stageDir,
    $portableDir,
    $portableZip,
    (Join-Path $artifactsDir "ExteraMonitor-Setup-v$Version-win-x64.exe"),
    (Join-Path $artifactsDir "SHA256SUMS.txt")
)) {
    if (Test-Path -LiteralPath $oldArtifact) {
        Remove-Item -LiteralPath $oldArtifact -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

dotnet publish (Join-Path $projectRoot "ExteraMonitor.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
Get-ChildItem -LiteralPath $stageDir -Filter *.pdb -File -Recurse | Remove-Item -Force

Copy-Item -LiteralPath $stageDir -Destination $portableDir -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination (Join-Path $portableDir "README.md")
Compress-Archive -Path $portableDir -DestinationPath $portableZip -CompressionLevel Optimal

& $innoCompiler "/DAppVersion=$Version" "/DSourceDir=$stageDir" "/DOutputDir=$artifactsDir" (Join-Path $PSScriptRoot "ExteraMonitor.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$installer = Join-Path $artifactsDir "ExteraMonitor-Setup-v$Version-win-x64.exe"
$hashLines = foreach ($artifact in @($installer, $portableZip)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($artifact))"
}
$hashLines | Set-Content -LiteralPath (Join-Path $artifactsDir "SHA256SUMS.txt") -Encoding ascii

Get-Item -LiteralPath $installer, $portableZip, (Join-Path $artifactsDir "SHA256SUMS.txt") |
    Select-Object Name, Length, LastWriteTime
