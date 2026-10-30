param([string]$InstallRoot)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
if (-not $InstallRoot) { $InstallRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\ReelGoodData\LaughDetection")) }
$pythonDir = Join-Path $InstallRoot "python"
$sitePackages = Join-Path $pythonDir "Lib\site-packages"
$assetsDir = Join-Path $InstallRoot "yamnet-cache"
$downloadDir = Join-Path $InstallRoot "downloads"
$tempDir = Join-Path $InstallRoot "temp"
$pipCacheDir = Join-Path $InstallRoot "pip-cache"
New-Item -ItemType Directory -Force -Path $InstallRoot, $pythonDir, $sitePackages, $assetsDir, $downloadDir, $tempDir, $pipCacheDir | Out-Null
$env:TEMP = $tempDir
$env:TMP = $tempDir
$env:PIP_CACHE_DIR = $pipCacheDir
$pth = Join-Path $pythonDir "python312._pth"
$py = Join-Path $pythonDir "python.exe"
if (-not (Test-Path -LiteralPath $py)) {
    Write-Output "Preparing Python 3.12 runtime..."
    $pythonZip = Join-Path $downloadDir "python-3.12.10-embed-amd64.zip"
    Invoke-WebRequest "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip" -OutFile $pythonZip
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $pythonDir -Force
}
$pthLines = @(Get-Content -LiteralPath $pth | Where-Object { $_ -notmatch "^\s*#|^\s*import site\s*$" })
if ($pthLines -notcontains "Lib\site-packages") { $pthLines += "Lib\site-packages" }
$pthLines += "import site"
Set-Content -LiteralPath $pth -Value ($pthLines -join [Environment]::NewLine) -Encoding ascii
if (-not (Test-Path (Join-Path $sitePackages "pip"))) {
    Write-Output "Preparing the package installer..."
    $getPip = Join-Path $downloadDir "get-pip.py"
    Invoke-WebRequest "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip
    & $py $getPip --disable-pip-version-check
    if ($LASTEXITCODE -ne 0) { throw "Python package installer setup failed." }
}
Write-Output "Installing the isolated TensorFlow runtime..."
& $py -m pip install --disable-pip-version-check --no-warn-script-location -r (Join-Path $PSScriptRoot "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Audio event model runtime installation failed." }
New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot "src") | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "src") | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallRoot "src") -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "THIRD-PARTY-NOTICES.txt") -Destination $InstallRoot -Force
$env:TFHUB_CACHE_DIR = $assetsDir
Write-Output "Preparing the 521-class audio event model..."
& $py (Join-Path $InstallRoot "src\yamnet_scan.py") --prepare-model
if ($LASTEXITCODE -ne 0) { throw "Audio event model download or setup failed." }
Set-Content -LiteralPath (Join-Path $InstallRoot "yamnet-ready.marker") -Value "YAMNet audio event scanner ready" -Encoding ascii
Write-Output "Audio event model setup complete."
