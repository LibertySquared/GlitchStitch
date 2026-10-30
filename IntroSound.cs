using System.Reflection;
using NAudio.Wave;

namespace ReelGood;

internal static class IntroSound
{
    // Device setup happens off the UI thread. Audio failure must never block startup.
    public static async Task PlayAsync(CancellationToken cancellation)
    {
        try
        {
            await Task.Run(async () =>
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReelGood.Audio.Intro.wav");
                if (stream is null || cancellation.IsCancellationRequested) return;
                using var wave = new WaveFileReader(stream);
                using var output = new WaveOutEvent { DesiredLatency = 80 };
                output.Init(wave);
                output.Volume = .7f;
                cancellation.ThrowIfCancellationRequested();
                output.Play();
                try
                {
                    while (output.PlaybackState == PlaybackState.Playing)
                        await Task.Delay(25, cancellation).ConfigureAwait(false);
                }
                finally { output.Stop(); }
            }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Muted, disconnected, or unavailable audio never interrupts the editor. */ }
    }
}
