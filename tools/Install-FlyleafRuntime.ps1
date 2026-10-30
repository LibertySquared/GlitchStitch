param([string]$LocalArchive)

$ErrorActionPreference = "Stop"
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$destination = [IO.Path]::GetFullPath((Join-Path $projectRoot "Resources\Flyleaf\FFmpeg"))
$expectedDestination = Join-Path $projectRoot "Resources\Flyleaf\FFmpeg"
$staging = [IO.Path]::GetFullPath((Join-Path $projectRoot ".build-profile\flyleaf-runtime-staging"))
$expectedStaging = Join-Path $projectRoot ".build-profile\flyleaf-runtime-staging"

if ($destination -ne $expectedDestination -or $staging -ne $expectedStaging) {
    throw "A Flyleaf runtime path resolved outside the ReelGood project. No files were changed."
}

$requiredFiles = @(
    "avcodec-63.dll",
    "avdevice-63.dll",
    "avfilter-12.dll",
    "avformat-63.dll",
    "avutil-61.dll",
    "swresample-7.dll",
    "swscale-10.dll"
)

if ((Test-Path -LiteralPath $destination) -and
    (($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf) }).Count -eq 0)) {
    Write-Output "Flyleaf FFmpeg runtime is already installed."
    return
}

$archiveUrl = "https://github.com/SuRGeoNix/Flyleaf/releases/download/v3.11.8/Flyleaf_v3.11.8.7z"
$expectedHash = "70bead90305f2293fd15a9f147d0f6962d6796579aa40a8307dc3831864eb095"
$archivePath = Join-Path $staging "Flyleaf_v3.11.8.7z"
$extractPath = Join-Path $staging "extract"
$null = New-Item -ItemType Directory -Force -Path $staging
$null = New-Item -ItemType Directory -Force -Path $extractPath

try {
    if ([string]::IsNullOrWhiteSpace($LocalArchive)) {
        Write-Output "Downloading the verified Flyleaf playback runtime…"
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
    }
    else {
        $resolvedLocalArchive = [IO.Path]::GetFullPath((Join-Path (Get-Location) $LocalArchive))
        if (-not $resolvedLocalArchive.StartsWith($projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "A provided Flyleaf archive must be inside the ReelGood project folder."
        }
        if (-not (Test-Path -LiteralPath $resolvedLocalArchive -PathType Leaf)) {
            throw "The provided Flyleaf archive was not found: $resolvedLocalArchive"
        }
        Copy-Item -LiteralPath $resolvedLocalArchive -Destination $archivePath -Force
    }
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "The Flyleaf release checksum did not match. The downloaded archive will not be used."
    }

    $tar = Join-Path $env:WINDIR "System32\tar.exe"
    if (-not (Test-Path -LiteralPath $tar)) { throw "Windows tar.exe is required to unpack the verified Flyleaf release." }
    $tarArguments = @("-xf", $archivePath, "-C", $extractPath) + @($requiredFiles | ForEach-Object { "FFmpeg/$_" })
    & $tar @tarArguments
    if ($LASTEXITCODE -ne 0) { throw "Could not unpack the Flyleaf FFmpeg runtime." }

    $sourceRuntime = Join-Path $extractPath "FFmpeg"
    $missing = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sourceRuntime $_) -PathType Leaf) })
    if ($missing.Count -gt 0) { throw "Flyleaf runtime archive is missing required files: $($missing -join ', ')" }

    $null = New-Item -ItemType Directory -Force -Path $destination
    foreach ($name in $requiredFiles) {
        Copy-Item -LiteralPath (Join-Path $sourceRuntime $name) -Destination (Join-Path $destination $name) -Force
    }
    Write-Output "Flyleaf FFmpeg runtime installed under Resources\Flyleaf\FFmpeg."
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $resolvedStaging = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $staging).Path)
        if ($resolvedStaging -ne $expectedStaging) { throw "Refusing to remove an unexpected Flyleaf staging directory: $resolvedStaging" }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
