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

Write-Host "Модели готовы: $poseModel и $tableModel"
