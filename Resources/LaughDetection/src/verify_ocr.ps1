param([string]$Manifest, [string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.DataWriter,Windows.Storage.Streams,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
$taskMethod=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetGenericArguments().Count -eq 1 } | Select-Object -First 1
function Wait-Operation($Operation,[Type]$Type) {
    $task=$taskMethod.MakeGenericMethod($Type).Invoke($null,@($Operation))
    $task.Wait()
    return $task.Result
}
$engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if ($null -eq $engine) {
    [IO.File]::WriteAllText($OutputPath,'{"available":false,"error":"No Windows OCR language is available.","images":[]}',[Text.UTF8Encoding]::new($false))
    exit 0
}
$results=[Collections.Generic.List[object]]::new()
foreach ($image in (Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json).images) {
    $stream=$null; $bitmap=$null; $writer=$null
    try {
        $stream=[Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
        $writer=[Windows.Storage.Streams.DataWriter]::new($stream)
        $writer.WriteBytes([IO.File]::ReadAllBytes($image.path))
        $null=Wait-Operation ($writer.StoreAsync()) ([uint32])
        $null=$writer.DetachStream(); $writer.Dispose(); $writer=$null
        $stream.Seek(0)
        $decoder=Wait-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap=Wait-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $recognized=Wait-Operation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $words=@(foreach ($line in $recognized.Lines) { foreach ($word in $line.Words) {
            @{text=$word.Text; x=$word.BoundingRect.X; y=$word.BoundingRect.Y; width=$word.BoundingRect.Width; height=$word.BoundingRect.Height}
        } })
        $results.Add(@{candidate=$image.candidate; phase=$image.phase; path=$image.path; text=$recognized.Text; width=$bitmap.PixelWidth; height=$bitmap.PixelHeight; words=$words})
    } catch {
        $results.Add(@{candidate=$image.candidate; phase=$image.phase; path=$image.path; error=$_.Exception.Message; words=@()})
    } finally {
        if ($null -ne $writer) { $writer.Dispose() }
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}
[IO.File]::WriteAllText($OutputPath,(@{available=$true;images=$results.ToArray()} | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
