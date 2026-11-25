$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDirectory = Join-Path $repoRoot 'models'
New-Item -ItemType Directory -Path $modelsDirectory -Force | Out-Null

$rfdetrModel = Join-Path $modelsDirectory 'rfdetr-keypoint-preview.onnx'
if (-not (Test-Path -LiteralPath $rfdetrModel)) {
    throw "Не найдена подготовленная модель RF-DETR: $rfdetrModel"
}
if ((Get-FileHash -LiteralPath $rfdetrModel -Algorithm SHA256).Hash -ne '515DDB658CFA19327EC9FA6152E0ED05D77AB94942B1208B6F998334C1A7E9C7') {
    throw "Контрольная сумма модели RF-DETR Keypoint не совпадает: $rfdetrModel"
}

$tableModel = Join-Path $modelsDirectory 'yolo11x.onnx'
if (-not (Test-Path -LiteralPath $tableModel)) {
    $modelUrl = 'https://huggingface.co/unileon-robotics/YOLO11-ONNX/resolve/main/yolo11x.onnx'
    Invoke-WebRequest -Uri $modelUrl -OutFile $tableModel
}

Write-Host "Модели готовы: $rfdetrModel и $tableModel"
