namespace CrystalGlow.Dmx;

/// <summary>
/// Repeatedly sends the universe's current frame out through an
/// IDmxOutput at a fixed interval. DMX fixtures only hold their state
/// while frames keep arriving, so this has to run continuously, not just
/// whenever a value changes.
/// </summary>
public class DmxTransmitLoop : IDisposable
{
    private readonly DmxUniverse _universe;
    private readonly IDmxOutput _output;
    private readonly int _intervalMs;

    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <summary>Raised (off the UI thread) if a Send call throws, e.g. the dongle was unplugged.</summary>
    public event Action<Exception>? SendFailed;

    public DmxTransmitLoop(DmxUniverse universe, IDmxOutput output, int intervalMs = 25)
    {
        _universe = universe;
        _output = output;
        _intervalMs = intervalMs;
    }

    public void Start()
    {
        if (_task != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _task = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _output.Send(_universe.Snapshot());
                }
                catch (Exception ex)
                {
                    SendFailed?.Invoke(ex);
                }

                try
                {
                    await Task.Delay(_intervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    // Expected on Stop().
                }
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait(500);
        }
        catch
        {
            // Ignore -- we're shutting down anyway.
        }

        _task = null;
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _output.Dispose();
    }
}
