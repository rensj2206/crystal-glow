using NAudio.Wave;

namespace CrystalGlow.Dmx;

/// <summary>
/// Live "sound to light" via WASAPI loopback capture -- listens to
/// whatever the PC is currently outputting (any app, any source), not a
/// specific uploaded file. Isolates bass frequencies with a simple
/// low-pass filter, follows their envelope (fast attack / slow release,
/// like a VU meter peak with decay -- gives a "flash then fade" feel),
/// and adaptively normalizes against the recent peak so it doesn't need
/// manual sensitivity tuning per song.
///
/// Shared by the console prototype (CrystalGlow.SoundReactive) and the
/// WPF app, so the DSP logic lives in one place.
///
/// Current limitation: bass isolation only (not full frequency
/// analysis), and the normalization floor is silence (0), not an
/// adaptive noise floor -- good enough to prove the concept, not a
/// finished audio engine.
/// </summary>
public class SoundReactiveEngine : IDisposable
{
    private const double CutoffHz = 150.0;
    private const double AttackSeconds = 0.005;
    private const double ReleaseSeconds = 0.300;
    private const double PeakMemorySeconds = 8.0;

    private WasapiLoopbackCapture? _capture;
    private double _lpAlpha;
    private double _lpState;
    private double _attackCoeff;
    private double _releaseCoeff;
    private double _envelope;
    private double _peakDecayCoeff;
    private double _recentPeak = 0.0001;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Multiplier applied after peak-normalization, before clamping to 0-255.
    /// 1.0 is neutral (relies purely on the adaptive peak). Above 1.0 makes
    /// quieter bass hit harder (and clips loud ones at full intensity
    /// sooner); below 1.0 requires louder bass to reach full intensity.
    /// Safe to change live while running.
    /// </summary>
    public double Sensitivity { get; set; } = 1.0;

    /// <summary>Raised on the capture thread with the latest 0-255 intensity value.</summary>
    public event Action<byte>? LevelChanged;

    public void Start()
    {
        if (IsRunning)
            return;

        _capture = new WasapiLoopbackCapture();
        var format = _capture.WaveFormat;

        var rc = 1.0 / (2 * Math.PI * CutoffHz);
        var dt = 1.0 / format.SampleRate;
        _lpAlpha = dt / (rc + dt);
        _lpState = 0;

        _attackCoeff = Math.Exp(-1.0 / (format.SampleRate * AttackSeconds));
        _releaseCoeff = Math.Exp(-1.0 / (format.SampleRate * ReleaseSeconds));
        _envelope = 0;

        _peakDecayCoeff = Math.Exp(-1.0 / (format.SampleRate * PeakMemorySeconds));
        _recentPeak = 0.0001;

        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning || _capture is null)
            return;

        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _capture = null;
        IsRunning = false;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null)
            return;

        var format = _capture.WaveFormat;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
        var bytesPerSample = format.BitsPerSample / 8;
        var channels = format.Channels;
        var frameSize = bytesPerSample * channels;
        var frameCount = e.BytesRecorded / frameSize;

        for (var i = 0; i < frameCount; i++)
        {
            var frameOffset = i * frameSize;
            double mono = 0;

            for (var ch = 0; ch < channels; ch++)
            {
                var sampleOffset = frameOffset + ch * bytesPerSample;
                double sample = isFloat
                    ? BitConverter.ToSingle(e.Buffer, sampleOffset)
                    : BitConverter.ToInt16(e.Buffer, sampleOffset) / 32768.0;
                mono += sample;
            }

            mono /= channels;

            // Low-pass filter -> bass-only signal.
            _lpState += _lpAlpha * (mono - _lpState);

            // Envelope follower over the filtered (bass) signal.
            var rectified = Math.Abs(_lpState);
            _envelope = rectified > _envelope
                ? _attackCoeff * _envelope + (1 - _attackCoeff) * rectified
                : _releaseCoeff * _envelope + (1 - _releaseCoeff) * rectified;

            // Adaptive peak tracking for normalization.
            _recentPeak = _envelope > _recentPeak
                ? _envelope
                : Math.Max(_recentPeak * _peakDecayCoeff, 0.0001);
        }

        var normalized = Math.Clamp(_envelope / _recentPeak * Sensitivity, 0.0, 1.0);
        LevelChanged?.Invoke((byte)(normalized * 255));
    }

    public void Dispose() => Stop();
}
