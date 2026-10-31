using System.Reflection;
using NAudio.Wave;

namespace GlitchStich;

internal static class NotificationSound
{
    private static int _playing;

    public static async Task PlayDraftReadyAsync(CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested || Interlocked.CompareExchange(ref _playing, 1, 0) != 0) return;
        try
        {
            await Task.Run(async () =>
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlitchStich.Audio.DraftReady.wav");
                if (stream is null || cancellation.IsCancellationRequested) return;
                using var wave = new WaveFileReader(stream);
                using var output = new WaveOutEvent { DesiredLatency = 80 };
                output.Init(wave);
                output.Volume = .65f;
                cancellation.ThrowIfCancellationRequested();
                output.Play();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    while (output.PlaybackState == PlaybackState.Playing)
                        await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                }
                finally { output.Stop(); }
            }, cancellation).ConfigureAwait(false);
        }
        catch (Exception) { /* Notification/device failure must not interrupt the draft or editor. */ }
        finally { Interlocked.Exchange(ref _playing, 0); }
    }
}
