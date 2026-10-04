param([string]$UnityEditor = "", [string]$VerifyPlugin = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (!$UnityEditor) {
    $UnityEditor = $env:UNITY_EDITOR_PATH
}
if (!$UnityEditor) {
    $editor = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '2022.3.*' } | Sort-Object Name -Descending | Select-Object -First 1
    if ($editor) { $UnityEditor = Join-Path $editor.FullName 'Editor\Unity.exe' }
}
if (!$UnityEditor -or !(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw 'Specify an installed, licensed Unity 2022.3 Editor with -UnityEditor or UNITY_EDITOR_PATH.'
}
$project = Join-Path $root 'bin\shader-project'
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Assets\Shaders'), (Join-Path $project 'Assets\Editor'), (Join-Path $project 'ProjectSettings') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Shaders\Source\VideoLayer.cginc') -Destination (Join-Path $project 'Assets\Shaders') -Force
Get-ChildItem -LiteralPath (Join-Path $root 'Shaders\Source') -Filter '*.shader' -File | Copy-Item -Destination (Join-Path $project 'Assets\Shaders') -Force
Copy-Item -LiteralPath (Join-Path $root 'Shaders\Editor\BuildVideoShaders.cs') -Destination (Join-Path $project 'Assets\Editor') -Force
$log = Join-Path $project 'build.log'
$arguments = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'BuildVideoShaders.BuildAndVerify', '-logFile', ('"' + $log + '"'))
if ($VerifyPlugin) {
    $pluginPath = (Resolve-Path -LiteralPath $VerifyPlugin).Path
    $arguments = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'BuildVideoShaders.VerifyEmbeddedPlugin', '-videoLayerPluginPath', ('"' + $pluginPath + '"'), '-logFile', ('"' + $log + '"'))
}
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Shader build/verification failed. See $log" }
if ($VerifyPlugin) { Write-Host 'Compiled plugin shader loading and rendering verified.'; return }
$bundle = Join-Path $project 'Output\videolayer.shaders'
if (!(Test-Path -LiteralPath $bundle)) { throw "Shader bundle was not created. See $log" }
Copy-Item -LiteralPath $bundle -Destination (Join-Path $root 'Shaders\videolayer.shaders') -Force
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath (Join-Path $root 'Shaders\Source') -File | Sort-Object Name | ForEach-Object { $hashes[$_.Name] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
$metadata = [ordered]@{ bundleSha256 = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash; sources = $hashes }
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'Shaders\bundle-info.json') -Encoding UTF8
Write-Host 'Shader bundle built and GPU pixel checks passed.'
