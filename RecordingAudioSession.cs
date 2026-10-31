using System.Diagnostics;
using System.IO;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace GlitchStich;

internal sealed class RecordingAudioSession
{
    private readonly List<CapturedTrack> _tracks = [];
    private readonly Stopwatch _clock = new();
    public IReadOnlyList<(string Path, string Title)> Files => _tracks.Select(t => (t.Path, t.Title)).ToArray();

    public void Start(string directory, bool systemAudio, bool microphone)
    {
        _clock.Start();
        if (systemAudio) Add(new WasapiLoopbackCapture(), directory, "system", "System audio");
        if (microphone) Add(new WasapiCapture(), directory, "microphone", "Microphone");
    }

    private void Add(WasapiCapture capture, string directory, string name, string title)
    {
        var track = new CapturedTrack(capture, System.IO.Path.Combine(directory, name + ".wav"), title, _clock);
        _tracks.Add(track);
        track.Start();
    }

    public async Task StopAsync()
    {
        _clock.Stop();
        foreach (var track in _tracks) track.Stop();
        await Task.WhenAll(_tracks.Select(t => t.FinishAsync()));
    }

    private sealed class CapturedTrack
    {
        private readonly WasapiCapture _capture;
        private readonly Stopwatch _clock;
        private readonly object _gate = new();
        private WaveFileWriter? _writer;
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;
        public string Path { get; }
        public string Title { get; }

        public CapturedTrack(WasapiCapture capture, string path, string title, Stopwatch clock)
        {
            _capture = capture; Path = path; Title = title; _clock = clock;
            _writer = new WaveFileWriter(path, capture.WaveFormat);
            capture.DataAvailable += (_, args) =>
            {
                lock (_gate)
                {
                    if (_writer is null) return;
                    // Loopback may emit no packets during silence. Keep that silence in
                    // the track rather than collapsing the timeline around the packets.
                    PadTo(Math.Max(0, _clock.Elapsed.TotalSeconds - args.BytesRecorded / (double)capture.WaveFormat.AverageBytesPerSecond));
                    _writer.Write(args.Buffer, 0, args.BytesRecorded);
                }
            };
            capture.RecordingStopped += (_, args) => Complete(args.Exception);
        }

        private void PadTo(double seconds)
        {
            if (_writer is null) return;
            var format = _capture.WaveFormat;
            var target = (long)(seconds * format.AverageBytesPerSecond) / format.BlockAlign * format.BlockAlign;
            var zeros = new byte[format.BlockAlign * 1024];
            while (_writer.Length < target)
                _writer.Write(zeros, 0, (int)Math.Min(zeros.Length, target - _writer.Length));
        }

        private void Complete(Exception? error)
        {
            lock (_gate)
            {
                try { PadTo(_clock.Elapsed.TotalSeconds); _writer?.Dispose(); }
                catch (Exception ex) { error ??= ex; }
                finally { _writer = null; }
            }
            if (error is null) _stopped.TrySetResult(); else _stopped.TrySetException(error);
        }

        public void Start() { _capture.StartRecording(); _started = true; }
        public void Stop()
        {
            if (!_started) { Complete(null); return; }
            try { _capture.StopRecording(); } catch (Exception ex) { Complete(ex); }
        }
        public async Task FinishAsync()
        {
            try { await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { _capture.Dispose(); }
        }
    }
}
