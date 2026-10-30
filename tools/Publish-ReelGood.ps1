param([switch]$PrepareOnly, [switch]$InstallPrepared)
$ErrorActionPreference = "Stop"

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot "portable-release"))
$stagingDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot ".reelgood-publish-staging"))
$buildDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot "bin"))
$expectedPublish = Join-Path $projectRoot "portable-release"
$expectedStaging = Join-Path $projectRoot ".reelgood-publish-staging"
$expectedBuild = Join-Path $projectRoot "bin"

if ($publishDirectory -ne $expectedPublish -or $stagingDirectory -ne $expectedStaging -or $buildDirectory -ne $expectedBuild) {
    throw "A generated app folder resolved outside the ReelGood project. No files were changed."
}
if (-not $PrepareOnly -and (Get-Process -Name ReelGood,GlitchStich -ErrorAction SilentlyContinue)) {
    throw "Close GlitchStich (or the previous ReelGood build) before publishing an update."
}

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
$dataDirectory = Join-Path $publishDirectory "ReelGoodData"
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null

$preserveStaging = $InstallPrepared.IsPresent
try {
    if (-not $InstallPrepared) {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }

    $env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:NUGET_PACKAGES = Join-Path $projectRoot ".nuget\packages"
    $env:APPDATA = Join-Path $projectRoot ".build-profile\Roaming"
    $env:LOCALAPPDATA = Join-Path $projectRoot ".build-profile\Local"
    New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    & (Join-Path $PSScriptRoot "Install-FlyleafRuntime.ps1")
    $assetsFile = Join-Path $projectRoot "obj\project.assets.json"
    $hasWinX64Assets = $false
    if (Test-Path -LiteralPath $assetsFile) {
        try {
            $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
            $hasWinX64Assets = @($assets.targets.PSObject.Properties.Name | Where-Object { $_ -match '^net9\.0-windows[^/]*/win-x64$' }).Count -gt 0
        } catch { $hasWinX64Assets = $false }
    }
    if (-not $hasWinX64Assets) {
        dotnet restore (Join-Path $projectRoot "ReelGood.csproj") `
            -r win-x64 `
            --configfile (Join-Path $projectRoot "NuGet.Config") `
            --ignore-failed-sources -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "ReelGood dependency restore failed with exit code $LASTEXITCODE." }
    }
    dotnet publish (Join-Path $projectRoot "ReelGood.csproj") `
        -c Release -r win-x64 --self-contained false --no-restore `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) { throw "ReelGood publish failed with exit code $LASTEXITCODE." }

    }
    $preserveStaging = $true
    # Keep managed assembly/resource identities stable so saved projects and
    # embedded playback/model assets remain compatible. Rename only the apphost.
    $stagedExecutable = Join-Path $stagingDirectory "GlitchStich.exe"
    $legacyAppHost = Join-Path $stagingDirectory "ReelGood.exe"
    if (-not (Test-Path -LiteralPath $stagedExecutable) -and (Test-Path -LiteralPath $legacyAppHost)) {
        Move-Item -LiteralPath $legacyAppHost -Destination $stagedExecutable
    }
    if (-not (Test-Path -LiteralPath $stagedExecutable)) { throw "The publish output did not contain GlitchStich.exe." }

    if ($PrepareOnly) { Write-Output "Prepared update: $stagingDirectory. Close ReelGood, then run this script with -InstallPrepared."; return }
    # Check the actual app files as well as the process list: isolated build sessions
    # may not see a desktop process, but Windows still enforces its file locks.
    foreach ($name in @("GlitchStich.exe", "ReelGood.exe", "ReelGood.dll")) {
        $target = Join-Path $publishDirectory $name
        if (Test-Path -LiteralPath $target) {
            try { $handle = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $handle.Dispose() }
            catch { throw "Close ReelGood before installing. The prepared update was retained; no app package files were replaced." }
        }
    }
    # Preserve user data; do not rewrite identical runtime files.
    foreach ($file in Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($stagingDirectory, $file.FullName)
        $target = [IO.Path]::GetFullPath((Join-Path $publishDirectory $relative))
        if (-not $target.StartsWith($publishDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Package file resolved outside the app folder." }
        if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) { continue }
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }

    $publishedHash = (Get-FileHash -LiteralPath $stagedExecutable -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath (Join-Path $publishDirectory "GlitchStich.exe") -Algorithm SHA256).Hash
    if ($publishedHash -ne $installedHash) { throw "The installed EXE does not match the published EXE." }

    foreach ($packageFile in @("ReelGood.dll", "NAudio.Core.dll", "NAudio.Wasapi.dll")) {
        $stagedFile = Join-Path $stagingDirectory $packageFile
        $installedFile = Join-Path $publishDirectory $packageFile
        if (-not (Test-Path -LiteralPath $stagedFile)) { throw "Required package file is missing: $packageFile" }
        if ((Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash) {
            throw "The installed application file does not match the published file: $packageFile"
        }
    }

    $legacyInstalledHost = Join-Path $publishDirectory 'ReelGood.exe'
    if (Test-Path -LiteralPath $legacyInstalledHost) { Remove-Item -LiteralPath $legacyInstalledHost -Force }
    $preserveStaging = $false
    Write-Output "Updated app: $(Join-Path $publishDirectory 'GlitchStich.exe')"
    Write-Output "SHA-256: $installedHash"
}
finally {
    # bin contains rebuildable app copies; portable-release is the only retained app package.
    if (Test-Path -LiteralPath $buildDirectory) {
        $resolvedBuild = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $buildDirectory).Path)
        if ($resolvedBuild -ne $expectedBuild) { throw "Refusing to remove unexpected build folder: $resolvedBuild" }
        Remove-Item -LiteralPath $resolvedBuild -Recurse -Force
    }
    if (-not $preserveStaging -and (Test-Path -LiteralPath $stagingDirectory)) {
        $resolvedStage = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $stagingDirectory).Path)
        if ($resolvedStage -ne $expectedStaging) { throw "Refusing to remove unexpected staging folder: $resolvedStage" }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
