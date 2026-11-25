param(
    [ValidateSet('s', 'm', 'l')]
    [string]$Variant = 'm',
    [double]$Second = 10,
    [int]$Runs = 10,
    [switch]$CompareFrames
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$experimentRoot = Join-Path $projectRoot '.playwright-mcp\rtmo'
$modelRoot = Join-Path $experimentRoot "rtmo-$Variant"
$zipPath = Join-Path $experimentRoot "rtmo-$Variant.zip"

$artifacts = @{
    s = @{ Url = 'https://download.openmmlab.com/mmpose/v1/projects/rtmo/onnx_sdk/rtmo-s_8xb32-600e_body7-640x640-dac2bf74_20231211.zip'; Sha256 = ''; Directory = 'rtmo-s' }
    m = @{ Url = 'https://download.openmmlab.com/mmpose/v1/projects/rtmo/onnx_sdk/rtmo-m_16xb16-600e_body7-640x640-39e78cc4_20231211.zip'; Sha256 = '6B9D3BE1323CC030444F731BBE593EA61F59B9481E23DA2F8B60CB4B72136E16'; Directory = 'rtmo-m' }
    l = @{ Url = 'https://download.openmmlab.com/mmpose/v1/projects/rtmo/onnx_sdk/rtmo-l_16xb16-600e_body7-640x640-b37118ce_20231211.zip'; Sha256 = ''; Directory = 'rtmo-l' }
}
$artifact = $artifacts[$Variant]
$modelRoot = Join-Path $experimentRoot $artifact.Directory
New-Item -ItemType Directory -Force -Path $experimentRoot | Out-Null
if (-not (Test-Path $zipPath)) { Invoke-WebRequest -Uri $artifact.Url -OutFile $zipPath }
if ($artifact.Sha256) {
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    if ($hash -ne $artifact.Sha256) { throw "Unexpected SHA-256 for ${zipPath}: $hash" }
}
if (-not (Test-Path (Join-Path $modelRoot 'end2end.onnx'))) {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $modelRoot -Force
}

docker build -t foodvision-rtmo-probe -f (Join-Path $PSScriptRoot 'Dockerfile') $projectRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$arguments = @(
    '--model', "/results/$($artifact.Directory)/end2end.onnx",
    '--model-name', $artifact.Directory,
    '--runs', $Runs
)
if ($CompareFrames) {
    $arguments += @(
        '--bgr-glob', '/work/.playwright-mcp/compare*.bgr',
        '--output', "/results/$($artifact.Directory)-comparison.json"
    )
} else {
    $arguments += @('--video', '/work/videos/Sample_Video_1.mp4', '--second', $Second)
}
docker run --rm --gpus all `
    -v "${projectRoot}:/work:ro" `
    -v "${experimentRoot}:/results" `
    foodvision-rtmo-probe @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
