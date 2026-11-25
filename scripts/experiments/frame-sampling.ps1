[CmdletBinding()]
param(
    [string] $VideoPath = 'videos/Sample_Video_1.mp4',
    [string] $OutputDirectory = '.playwright-mcp/pose-evaluation',
    [string] $ManifestPath = 'docs/validation/pose-evaluation-manifest.json',
    [string] $Image = 'foodvision-optimization-experiment'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The first interval straddles the known title-card-to-hall change.  The other
# intervals sample independent portions of the restaurant scene while retaining
# ten consecutive 1 fps frames for temporal/model comparisons.
$intervals = @(
    [pscustomobject]@{ Id = 'title-to-scene'; StartSeconds = 3;   FrameCount = 10 },
    [pscustomobject]@{ Id = 'early-scene';    StartSeconds = 25;  FrameCount = 10 },
    [pscustomobject]@{ Id = 'mid-scene-a';    StartSeconds = 65;  FrameCount = 10 },
    [pscustomobject]@{ Id = 'mid-scene-b';    StartSeconds = 105; FrameCount = 10 },
    [pscustomobject]@{ Id = 'late-scene-a';   StartSeconds = 145; FrameCount = 10 },
    [pscustomobject]@{ Id = 'late-scene-b';   StartSeconds = 185; FrameCount = 10 }
)

$width = 640
$height = 360
$channels = 3
$frameBytes = $width * $height * $channels

$videoFullPath = (Resolve-Path -LiteralPath $VideoPath).Path
$videoDirectory = Split-Path -Parent $videoFullPath
$videoFileName = Split-Path -Leaf $videoFullPath
$outputFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is required because the host does not provide a pinned ffmpeg binary.'
}

New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
$existingFrames = Get-ChildItem -LiteralPath $outputFullPath -Filter '*.bgr' -File
if ($existingFrames) {
    throw "Output directory already contains BGR frames: $outputFullPath. Use an empty directory to avoid replacing a comparison dataset."
}

$manifestFrames = [System.Collections.Generic.List[object]]::new()

foreach ($interval in $intervals) {
    $temporaryRawName = "$($interval.Id).raw"
    $temporaryRawPath = Join-Path $outputFullPath $temporaryRawName
    if (Test-Path -LiteralPath $temporaryRawPath) {
        throw "Temporary output already exists: $temporaryRawPath"
    }

    # -ss follows -i so seeking is accurate; this is deliberately CPU-only.
    & docker run --rm --entrypoint ffmpeg `
        --mount "type=bind,source=$videoDirectory,target=/input,readonly" `
        --mount "type=bind,source=$outputFullPath,target=/output" `
        $Image -hide_banner -loglevel error -i "/input/$videoFileName" `
        -ss $interval.StartSeconds -t $interval.FrameCount `
        -vf "fps=1,scale=${width}:${height}" -frames:v $interval.FrameCount `
        -f rawvideo -pix_fmt bgr24 "/output/$temporaryRawName"
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed for interval $($interval.Id) with exit code $LASTEXITCODE."
    }

    $rawBytes = [System.IO.File]::ReadAllBytes($temporaryRawPath)
    $expectedBytes = $interval.FrameCount * $frameBytes
    if ($rawBytes.Length -ne $expectedBytes) {
        throw "Interval $($interval.Id) produced $($rawBytes.Length) bytes; expected $expectedBytes for $($interval.FrameCount) frames."
    }

    for ($index = 0; $index -lt $interval.FrameCount; $index++) {
        $timestampSeconds = $interval.StartSeconds + $index
        $frameName = '{0}-{1:D3}s.bgr' -f $interval.Id, $timestampSeconds
        $framePath = Join-Path $outputFullPath $frameName
        $frame = New-Object byte[] $frameBytes
        [System.Buffer]::BlockCopy($rawBytes, $index * $frameBytes, $frame, 0, $frameBytes)
        [System.IO.File]::WriteAllBytes($framePath, $frame)

        $manifestFrames.Add([ordered]@{
            file = ".playwright-mcp/pose-evaluation/$frameName"
            interval = $interval.Id
            timestampSeconds = $timestampSeconds
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $framePath).Hash
            bytes = $frameBytes
        })
    }

    Remove-Item -LiteralPath $temporaryRawPath
}

$manifestDirectory = Split-Path -Parent $manifestFullPath
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
$manifest = [ordered]@{
    schemaVersion = 1
    source = [ordered]@{
        file = 'videos/Sample_Video_1.mp4'
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $videoFullPath).Hash
    }
    extraction = [ordered]@{
        tool = 'ffmpeg via foodvision-optimization-experiment Docker image'
        gpuUsed = $false
        seekMode = 'accurate (-ss after -i)'
        cadenceFps = 1
        pixelFormat = 'bgr24'
        width = $width
        height = $height
        bytesPerFrame = $frameBytes
    }
    intervals = @($intervals | ForEach-Object {
        [ordered]@{
            id = $_.Id
            startSeconds = $_.StartSeconds
            endSeconds = $_.StartSeconds + $_.FrameCount - 1
            frameCount = $_.FrameCount
        }
    })
    frames = @($manifestFrames)
}

[System.IO.File]::WriteAllText(
    $manifestFullPath,
    ($manifest | ConvertTo-Json -Depth 6),
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Created $($manifestFrames.Count) BGR frames in $outputFullPath"
Write-Host "Manifest: $manifestFullPath"
