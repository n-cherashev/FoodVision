$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDirectory = Join-Path $repoRoot 'models'
New-Item -ItemType Directory -Path $modelsDirectory -Force | Out-Null

$poseModel = Join-Path $modelsDirectory 'yolo11x-pose.onnx'
if (-not (Test-Path -LiteralPath $poseModel)) {
    throw "Не найдена подготовленная pose-модель: $poseModel"
}

$tableModel = Join-Path $modelsDirectory 'yolo11x.onnx'
if (-not (Test-Path -LiteralPath $tableModel)) {
    $modelUrl = 'https://huggingface.co/unileon-robotics/YOLO11-ONNX/resolve/main/yolo11x.onnx'
    Invoke-WebRequest -Uri $modelUrl -OutFile $tableModel
}

$personModel = Join-Path $modelsDirectory 'yolo26l-detect.onnx'
if (-not (Test-Path -LiteralPath $personModel)) {
    $modelUrl = 'https://huggingface.co/qtpose/yolo26l-detect-onnx/resolve/main/yolo26l_detect.onnx'
    Invoke-WebRequest -Uri $modelUrl -OutFile $personModel
}
if ((Get-FileHash -LiteralPath $personModel -Algorithm SHA256).Hash -ne 'E2BDA06E36639FDDFBBA0C4F4BE8405A6ACDBADF15B2F393B8F885FE0DB82FB3') {
    throw "Контрольная сумма модели YOLO26l не совпадает: $personModel"
}

Write-Host "Модели готовы: $poseModel, $tableModel и $personModel"
