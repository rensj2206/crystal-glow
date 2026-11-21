namespace CrystalGlow.Dmx;

/// <summary>
/// A transport that can push one DMX frame (512 channel bytes) out to
/// real hardware. Implementations: FtdiOpenDmxOutput (USB dongle),
/// NullDmxOutput (no hardware -- used when nothing is connected).
/// </summary>
public interface IDmxOutput : IDisposable
{
    void Send(ReadOnlySpan<byte> channels);
}
