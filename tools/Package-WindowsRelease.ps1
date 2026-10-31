param([string]$Version = '0.1.0-beta.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Use a semantic release version.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$work = Join-Path $projectRoot '.release-work'
$output = Join-Path $projectRoot 'release-assets'
$package = Join-Path $work 'GlitchStich'
function Remove-GeneratedFolder([string]$Path, [string]$ExpectedName) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved -ne (Join-Path $projectRoot $ExpectedName)) { throw 'Unexpected generated folder; refusing cleanup.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Push-Location $projectRoot
try {
    if (@(git status --porcelain).Count) { throw 'Commit source changes before packaging a public release.' }
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    Remove-GeneratedFolder $work '.release-work'
    New-Item -ItemType Directory -Force -Path $package, $output | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
    $env:TEMP = Join-Path $projectRoot '.build-profile\Temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
    & (Join-Path $PSScriptRoot 'Install-FlyleafRuntime.ps1')
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ffmpeg.exe'))) { & (Join-Path $PSScriptRoot 'Install-FFmpeg.ps1') }
    dotnet restore GlitchStich.csproj -r win-x64 -p:SelfContained=true --configfile NuGet.Config -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Release dependency restore failed.' }
    dotnet publish GlitchStich.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -p:Version=$Version -o $package
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    Get-ChildItem -LiteralPath $package -Filter '*.pdb' -File | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $package
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Resources\Audio\DraftReady-source.txt') -Destination (Join-Path $package 'Resources\DraftReady-source.txt')
    @"
GlitchStich $Version - Windows x64 public testing build

Extract the ENTIRE ZIP to a writable folder, then open GlitchStich.exe.
Keep all files and folders together. The .NET runtime is included.
Windows x64 with Direct3D 11 graphics drivers is required.
This build is unsigned. Verify its download and SHA-256 before running.

FFmpeg/FFprobe are embedded and prepared automatically beside the app.
First detector setup downloads Python dependencies and pretrained models;
internet access, additional disk space and setup time are required.
Glitchy also needs Ollama running locally with a suitable installed model.
YouTube is optional and uses your own Google desktop OAuth setup.

Import your own media, select editing modes or enter a prompt, then Start scan.
Review suggested glitches and draft stitches before applying or exporting them.
Save your project before closing. Settings and models live in GlitchStichData beside
the executable. Back up that folder and your projects before updating the app.

No personal media, projects, accounts, or developer preferences are bundled.
Report bugs: https://github.com/LibertySquared/GlitchStitch/issues
Source commit: $commit
Third-party licenses/notices are under Resources and tools; their own terms apply.
The MIT LICENSE applies to this project's original source.
"@ | Set-Content -LiteralPath (Join-Path $package 'START-HERE.txt') -Encoding utf8
    @{ version = $Version; sourceCommit = $commit; platform = 'win-x64'; selfContained = $true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release-info.json') -Encoding utf8
    foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($package, $file.FullName)
        if ($relative -match '(?i)(^|[\\/])(ReelGoodData|GlitchStichData|\.git|checks)[\\/]|\.(mp4|mkv|mov|wav|mp3|ogg|glitch|glitchstich|reelgood|log|pdb|pfx|p12|key)$|client_secret|credentials\.json|tokens?\.json') { throw "Private/generated file in package: $relative" }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $package 'GlitchStich.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.frameworks -or -not (Test-Path -LiteralPath (Join-Path $package 'coreclr.dll')) -or -not (Test-Path -LiteralPath (Join-Path $package 'PresentationFramework.dll'))) { throw 'The release is not self-contained.' }
    $zipName = "GlitchStich-$Version-win-x64.zip"
    $zip = Join-Path $output $zipName
    if (Test-Path -LiteralPath $zip) { throw 'A release ZIP already exists; use a new version or inspect the existing artifact.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    $verify = Join-Path $work 'verify'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $verify)
    $extracted = Join-Path $verify 'GlitchStich'
    $embeddedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $extracted 'GlitchStich.dll')).ProductVersion
    if ($embeddedVersion -ne $Version -and -not $embeddedVersion.StartsWith($Version + '+', [StringComparison]::Ordinal)) { throw "Embedded app version differs from release version: $embeddedVersion" }
    if ((Get-FileHash (Join-Path $package 'GlitchStich.dll')).Hash -ne (Get-FileHash (Join-Path $extracted 'GlitchStich.dll')).Hash) { throw 'ZIP round-trip changed the app assembly.' }
    $check = Start-Process -FilePath (Join-Path $extracted 'GlitchStich.exe') -ArgumentList '--verify-package' -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw 'Extracted release package failed verification.' }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $zipName" | Set-Content -LiteralPath (Join-Path $output "GlitchStich-$Version-SHA256SUMS.txt") -Encoding ascii
    Write-Output "VERIFIED RELEASE: $zip"
    Write-Output "SHA256: $hash"
    Remove-GeneratedFolder $work '.release-work'
    Remove-GeneratedFolder (Join-Path $projectRoot 'bin') 'bin'
}
finally { Pop-Location }
