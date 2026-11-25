namespace CrystalGlow.Dmx;

/// <summary>
/// One captured scene within a sequence: a snapshot of every channel's
/// value on a single fixture, plus how to transition into it. FadeSeconds
/// of 0 means a sharp cut (apply instantly); anything above that fades
/// linearly from whatever the fixture's channels currently read.
/// HoldSeconds only matters in auto-play -- how long to sit on this step
/// before advancing to the next one.
/// </summary>
public sealed class SequenceStep
{
    public required PatchedFixture Fixture { get; init; }
    public required Dictionary<ChannelDefinition, int> ChannelValues { get; init; }
    public double FadeSeconds { get; set; }
    public double HoldSeconds { get; set; } = 1.0;
    public string Label { get; set; } = "";

    public override string ToString() =>
        $"{Label} ({Fixture.Label}) — {(FadeSeconds <= 0 ? "sharp cut" : $"fade {FadeSeconds:0.0}s")}, hold {HoldSeconds:0.0}s";
}

/// <summary>
/// Plays an ordered list of SequenceSteps against the fixtures they were
/// captured from. Runs its own ~40Hz loop (same cadence as
/// DmxTransmitLoop) so a fade stays smooth regardless of what the UI
/// thread is doing. Advancing (manually via Next(), or automatically in
/// auto-play once a step's hold time elapses) always fades from the
/// fixture's *actual current* channel values, so interrupting a fade
/// part-way through and jumping to another step still looks continuous
/// rather than snapping back to some stored "previous step" value.
/// </summary>
public sealed class SequencePlayer : IDisposable
{
    private const int TickIntervalMs = 25;

    private readonly List<SequenceStep> _steps = new();
    private readonly object _lock = new();

    private Task? _loopTask;
    private CancellationTokenSource? _cts;

    private int _currentIndex = -1;
    private SequenceStep? _activeStep;
    private Dictionary<ChannelDefinition, int>? _fromValues;
    private DateTime _transitionStart;
    private bool _holding;
    private DateTime _holdStart;

    public IReadOnlyList<SequenceStep> Steps => _steps;
    public int CurrentIndex => _currentIndex;
    public bool IsRunning => _loopTask is not null;
    public bool IsAutoPlaying { get; private set; }

    /// <summary>Raised (on the sequence's own thread) whenever a new step starts.</summary>
    public event Action? StepStarted;

    public void AddStep(SequenceStep step)
    {
        lock (_lock)
            _steps.Add(step);
    }

    public void RemoveStep(SequenceStep step)
    {
        lock (_lock)
        {
            var index = _steps.IndexOf(step);
            if (index < 0)
                return;

            _steps.RemoveAt(index);

            if (_currentIndex >= _steps.Count)
                _currentIndex = _steps.Count - 1;
        }
    }

    /// <summary>Starts the background loop if it isn't already running, and begins at the first step if nothing is active yet.</summary>
    public void Start(bool autoPlay)
    {
        IsAutoPlaying = autoPlay;

        if (_loopTask is not null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loopTask = Task.Run(() => RunLoop(token), token);

        if (_currentIndex < 0 && _steps.Count > 0)
            BeginTransitionTo(0);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _loopTask = null;
        IsAutoPlaying = false;
    }

    /// <summary>Advances to the next step (wrapping to the first after the last), fading from current live values.</summary>
    public void Next()
    {
        lock (_lock)
        {
            if (_steps.Count == 0)
                return;

            var next = (_currentIndex + 1) % _steps.Count;
            BeginTransitionTo(next);
        }
    }

    private void BeginTransitionTo(int index)
    {
        var step = _steps[index];

        _fromValues = step.ChannelValues.Keys.ToDictionary(c => c, c => ReadCurrent(step.Fixture, c));
        _activeStep = step;
        _currentIndex = index;
        _transitionStart = DateTime.UtcNow;
        _holding = false;

        StepStarted?.Invoke();
    }

    private static int ReadCurrent(PatchedFixture fixture, ChannelDefinition channel) =>
        channel.Is16Bit ? fixture.GetChannel16(channel.Offset, channel.FineOffset!.Value) : fixture.GetChannel(channel.Offset);

    private void RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Tick();

            try
            {
                Task.Delay(TickIntervalMs, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Tick()
    {
        SequenceStep? step;
        Dictionary<ChannelDefinition, int>? fromValues;

        lock (_lock)
        {
            step = _activeStep;
            fromValues = _fromValues;
        }

        if (step is null || fromValues is null)
            return;

        var elapsed = (DateTime.UtcNow - _transitionStart).TotalSeconds;
        var t = step.FadeSeconds <= 0 ? 1.0 : Math.Clamp(elapsed / step.FadeSeconds, 0.0, 1.0);

        foreach (var (channel, target) in step.ChannelValues)
        {
            var from = fromValues.GetValueOrDefault(channel, target);
            var value = (int)Math.Round(from + (target - from) * t);

            if (channel.Is16Bit)
                step.Fixture.SetChannel16(channel.Offset, channel.FineOffset!.Value, (ushort)value);
            else
                step.Fixture.SetChannel(channel.Offset, (byte)value);
        }

        if (t >= 1.0 && !_holding)
        {
            _holding = true;
            _holdStart = DateTime.UtcNow;
        }

        if (_holding && IsAutoPlaying && (DateTime.UtcNow - _holdStart).TotalSeconds >= step.HoldSeconds)
            Next();
    }

    public void Dispose() => Stop();
}
