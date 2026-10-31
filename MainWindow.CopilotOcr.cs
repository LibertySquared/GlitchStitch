using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GlitchStich;

public partial class MainWindow
{
    private async Task<string> ReadCopilotScreenTextAsync(double start, double length, string folder, CancellationToken cancellation)
    {
        var script = Path.Combine(Path.GetDirectoryName(typeof(MainWindow).Assembly.Location)!, "Resources", "LaughDetection", "src", "verify_ocr.ps1");
        if (!File.Exists(script)) return "Local screen text recognition is unavailable.";
        var pattern = Path.Combine(folder, "ocr-%02d.jpg");
        string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        await RunExportProcessAsync(["-y", "-v", "error", "-threads", "2", "-ss", N(start), "-i", _videoPath!, "-t", N(length), "-an", "-vf", "select='gte(t,0)*(isnan(prev_selected_t)+gte(t-prev_selected_t,3))',scale=1600:-2", "-fps_mode", "vfr", "-frames:v", "4", "-q:v", "4", pattern], cancellation, _ => { });
        var frames = Directory.GetFiles(folder, "ocr-*.jpg").Order().ToArray();
        var manifest = Path.Combine(folder, "ocr-input.json"); var output = Path.Combine(folder, "ocr-output.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new { images = frames.Select((path, i) => new { path, candidate = i, phase = "event" }) }), cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Manifest", manifest, "-OutputPath", output }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return "Screen text recognition timed out; do not assume HUD details were confirmed."; }
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || !File.Exists(output)) return "Screen text could not be recognized; do not invent HUD evidence.";
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(output, cancellation));
        var text = new StringBuilder();
        foreach (var image in document.RootElement.GetProperty("images").EnumerateArray())
        {
            if (!image.TryGetProperty("width", out var width) || !image.TryGetProperty("words", out var words)) continue;
            var w = width.GetDouble(); var h = image.GetProperty("height").GetDouble();
            var central = words.EnumerateArray().Where(word =>
            {
                var x = (word.GetProperty("x").GetDouble() + word.GetProperty("width").GetDouble() / 2) / w;
                var y = (word.GetProperty("y").GetDouble() + word.GetProperty("height").GetDouble() / 2) / h;
                return x >= .2 && x <= .8 && y >= .08 && y <= .95;
            }).Select(word => word.GetProperty("text").GetString());
            var line = string.Join(" ", central); if (line.Length > 1000) line = line[..1000];
            text.AppendLine($"Offset {image.GetProperty("candidate").GetInt32() * 3}s: {line}");
        }
        return text.Length == 0 ? "No readable central screen text was found." : text.ToString();
    }
}
