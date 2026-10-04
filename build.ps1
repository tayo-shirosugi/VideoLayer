param(
    [string]$BeatSaberDir = "",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$shaderBundle = Join-Path $Root 'Shaders\videolayer.shaders'
$shaderInfoPath = Join-Path $Root 'Shaders\bundle-info.json'
if (!(Test-Path -LiteralPath $shaderBundle) -or !(Test-Path -LiteralPath $shaderInfoPath)) {
    throw 'The bundled VideoLayer shaders are missing. Run build-shaders.ps1 first.'
}
$shaderInfo = Get-Content -LiteralPath $shaderInfoPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $shaderBundle -Algorithm SHA256).Hash -ne $shaderInfo.bundleSha256) {
    throw 'Shader bundle hash mismatch. Run build-shaders.ps1.'
}
foreach ($source in $shaderInfo.sources.PSObject.Properties) {
    $sourcePath = Join-Path $Root (Join-Path 'Shaders\Source' $source.Name)
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $source.Value) {
        throw "Shader source changed: $($source.Name). Run build-shaders.ps1."
    }
}
$manifest = Join-Path $Root "manifest.json"
$manifestData = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
[xml]$project = Get-Content -LiteralPath (Join-Path $Root "VideoLayer.csproj") -Raw
if ($manifestData.version -ne $project.Project.PropertyGroup.Version) {
    throw "Version mismatch between manifest.json and VideoLayer.csproj."
}

# Prefer an explicit path. A typo must not silently build against another game version.
if (!$BeatSaberDir) { $BeatSaberDir = $env:BEATSABER_DIR }
if (!$BeatSaberDir) {
    $localFile = Join-Path $Root ".local.beatsaber.path"
    if (Test-Path -LiteralPath $localFile) { $BeatSaberDir = (Get-Content -LiteralPath $localFile -Raw).Trim() }
}
if (!$BeatSaberDir) {
    foreach ($candidate in @("C:\Program Files (x86)\Steam\steamapps\common\Beat Saber", "C:\Program Files\Steam\steamapps\common\Beat Saber")) {
        if (Test-Path -LiteralPath (Join-Path $candidate "Beat Saber_Data\Managed\IPA.Loader.dll")) {
            $BeatSaberDir = $candidate
            break
        }
    }
}
if (!$BeatSaberDir -or !(Test-Path -LiteralPath $BeatSaberDir -PathType Container)) {
    throw "Beat Saber folder was not found. Pass -BeatSaberDir or set BEATSABER_DIR."
}
$BeatSaberDir = (Resolve-Path -LiteralPath $BeatSaberDir).Path
$referencePaths = @($project.Project.ItemGroup.Reference | ForEach-Object {
    if ($_.HintPath) { $_.HintPath.Replace('$(BeatSaberDir)', $BeatSaberDir) }
})
foreach ($reference in $referencePaths) {
    if (!(Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Required reference DLL missing: $reference" }
}
$binDir = Join-Path $Root "bin\$Configuration\net472"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
$dll = Join-Path $binDir "VideoLayer.dll"
$hasSdk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdkList = & dotnet --list-sdks
    $hasSdk = $LASTEXITCODE -eq 0 -and !!$sdkList
}
if ($hasSdk) {
    & dotnet build (Join-Path $Root "VideoLayer.csproj") -c $Configuration "-p:BeatSaberDir=$BeatSaberDir" --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with code $LASTEXITCODE" }
} else {
    $compiler = $null
    $mono = $null
    foreach ($editor in @(Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue)) {
        $candidateMono = Join-Path $editor.FullName "Editor\Data\MonoBleedingEdge\bin\mono.exe"
        $candidateCompiler = Join-Path $editor.FullName "Editor\Data\MonoBleedingEdge\lib\mono\msbuild\Current\bin\Roslyn\csc.exe"
        if ((Test-Path -LiteralPath $candidateMono) -and (Test-Path -LiteralPath $candidateCompiler)) {
            $mono = $candidateMono
            $compiler = $candidateCompiler
            break
        }
    }
    if (!$compiler) { throw "Install a .NET SDK or a Unity Editor with the Roslyn compiler. The legacy Framework compiler is unsupported." }
    $managed = Join-Path $BeatSaberDir "Beat Saber_Data\Managed"
    $sourceFiles = @(Get-ChildItem -LiteralPath $Root -Filter "*.cs" -File) +
        @(Get-ChildItem -LiteralPath (Join-Path $Root "Patches") -Filter "*.cs" -File) +
        @(Get-ChildItem -LiteralPath (Join-Path $Root "UI") -Filter "*.cs" -File)
    $compilerArgs = @($compiler, "/target:library", "/langversion:latest", "/out:$dll",
        "/resource:$manifest,VideoLayer.manifest.json",
        "/resource:$(Join-Path $Root 'UI\settings.bsml'),VideoLayer.UI.settings.bsml",
        "/resource:$shaderBundle,VideoLayer.Shaders")
    if ($Configuration -eq "Release") { $compilerArgs += "/optimize+" } else { $compilerArgs += "/debug:portable" }
    $compilerArgs += @($referencePaths | ForEach-Object { "/r:$_" })
    $compilerArgs += @("mscorlib", "System", "System.Core", "netstandard" | ForEach-Object { "/r:$(Join-Path $managed "$_.dll")" })
    $compilerArgs += @($sourceFiles | ForEach-Object { $_.FullName })
    & $mono $compilerArgs
    if ($LASTEXITCODE -ne 0) { throw "Unity Roslyn compile failed with code $LASTEXITCODE" }
}
if (!(Test-Path -LiteralPath $dll)) { throw "Build output missing: $dll" }
& (Join-Path $Root "package.ps1") -Configuration $Configuration
if ($Install) {
    $plugins = Join-Path $BeatSaberDir "Plugins"
    Copy-Item -LiteralPath $dll -Destination (Join-Path $plugins "VideoLayer.dll") -Force
    Write-Host "Installed plugin. If upgrading from VideoLayer4CameraPlus.dll, remove the old DLL manually."
}
Write-Host "Build succeeded: $dll"
