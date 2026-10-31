param([string]$InstallRoot)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
if (-not $InstallRoot) { $InstallRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\portable-release\GlitchStichData\LaughDetection")) }
$python = Join-Path $InstallRoot "python\python.exe"
if (-not (Test-Path -LiteralPath $python)) { throw "The local Python runtime is missing. Run the audio scan setup first." }
$modelDir = Join-Path $InstallRoot "clip-video"
$onnxDir = Join-Path $modelDir "onnx"
$pipCache = Join-Path $InstallRoot "pip-cache"
$tempRoot = Join-Path $InstallRoot "temp"
New-Item -ItemType Directory -Force -Path $onnxDir, $pipCache, $tempRoot | Out-Null
$env:PIP_CACHE_DIR = $pipCache
$env:TEMP = $tempRoot
$env:TMP = $tempRoot
Write-Output "Preparing the local visual matching runtime..."
$providerCheck = & $python -c "import importlib,importlib.util; ort=importlib.import_module('onnxruntime') if importlib.util.find_spec('onnxruntime') else None; print('CUDA' if ort and 'CUDAExecutionProvider' in ort.get_available_providers() else 'CPU')"
if ($LASTEXITCODE -ne 0 -or $providerCheck -notcontains "CUDA") {
    Write-Output "Installing the NVIDIA CUDA inference runtime on the GlitchStich drive..."
    & $python -m pip uninstall -y onnxruntime onnxruntime-gpu | Out-Null
    & $python -m pip install --disable-pip-version-check --no-warn-script-location "onnxruntime-gpu[cuda,cudnn]==1.22.0" Pillow==11.3.0 tokenizers==0.21.4
    if ($LASTEXITCODE -ne 0) {
        Write-Output "CUDA runtime setup was unavailable; installing the compatible CPU fallback..."
        & $python -m pip uninstall -y onnxruntime onnxruntime-gpu | Out-Null
        & $python -m pip install --disable-pip-version-check --no-warn-script-location onnxruntime==1.22.0 Pillow==11.3.0 tokenizers==0.21.4
        if ($LASTEXITCODE -ne 0) { throw "Visual matching runtime installation failed." }
    }
}
$model = Join-Path $onnxDir "model_quantized.onnx"
if (-not (Test-Path -LiteralPath $model)) {
    Write-Output "Downloading the 153 MB visual model to the app drive..."
    Invoke-WebRequest "https://huggingface.co/onnx-community/clip-vit-base-patch32-ONNX/resolve/b318363e9c2fe791fd691d44862bb4cd4d7accc5/onnx/model_quantized.onnx" -OutFile $model
}
$actualHash = (Get-FileHash -LiteralPath $model -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne "06e11583385faecfa7273f7cc72b603be915ab9340269482cde69a5d0f5e3350") { Remove-Item -LiteralPath $model -Force; throw "The visual model download did not pass its integrity check." }
$tokenizer = Join-Path $modelDir "tokenizer.json"
if (-not (Test-Path -LiteralPath $tokenizer)) { Invoke-WebRequest "https://huggingface.co/onnx-community/clip-vit-base-patch32-ONNX/resolve/b318363e9c2fe791fd691d44862bb4cd4d7accc5/tokenizer.json" -OutFile $tokenizer }
$workerDir = Join-Path $InstallRoot "src"
New-Item -ItemType Directory -Force -Path $workerDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src\clip_video_scan.py") -Destination $workerDir -Force
$providerCheck = & $python -c "import importlib,importlib.util; ort=importlib.import_module('onnxruntime') if importlib.util.find_spec('onnxruntime') else None; print('CUDA' if ort and 'CUDAExecutionProvider' in ort.get_available_providers() else 'CPU')"
if ($providerCheck -contains "CUDA") {
    Set-Content -LiteralPath (Join-Path $InstallRoot "clip-video-ready.marker") -Value "GPU-capable CLIP scanner ready" -Encoding ascii
    Write-Output "Visual scanner ready; NVIDIA CUDA will be used when available, with CPU fallback."
} else {
    Set-Content -LiteralPath (Join-Path $InstallRoot "clip-video-ready.marker") -Value "CPU fallback CLIP scanner ready" -Encoding ascii
    Write-Output "Visual scanner ready with CPU fallback; CUDA was not available in this Python runtime."
}
