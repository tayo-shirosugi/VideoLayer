param([ValidateSet("Debug", "Release")][string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root "manifest.json") -Raw | ConvertFrom-Json
[xml]$project = Get-Content -LiteralPath (Join-Path $root "VideoLayer.csproj") -Raw
if ($manifest.version -ne $project.Project.PropertyGroup.Version) { throw "Version mismatch." }
if ($manifest.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Invalid release version." }
$dll = Join-Path $root "bin\$Configuration\net472\VideoLayer.dll"
if (!(Test-Path -LiteralPath $dll)) { throw "Build the plugin before packaging: $dll" }
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
# A unique staging directory prevents unrelated files from entering a release.
$staging = Join-Path $dist ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path (Join-Path $staging "Plugins") -Force | Out-Null
try {
    Copy-Item -LiteralPath $dll -Destination (Join-Path $staging "Plugins\VideoLayer.dll")
    Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $staging
    $zipPath = Join-Path $dist "VideoLayer-v$($manifest.version).zip"
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force
    Write-Host "Created release package: $zipPath"
} finally {
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    $resolvedDist = (Resolve-Path -LiteralPath $dist).Path
    if (!$resolvedStaging.StartsWith($resolvedDist + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Staging directory is outside dist."
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
