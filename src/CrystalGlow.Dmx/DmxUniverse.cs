namespace CrystalGlow.Dmx;

/// <summary>
/// A single DMX512 universe: 512 channels, each one byte (0-255).
/// This is just the in-memory buffer — sending it to real hardware
/// (USB dongle, Art-Net, sACN) is a separate concern, added later.
/// </summary>
public class DmxUniverse
{
    public const int ChannelCount = 512;

    private readonly byte[] _channels = new byte[ChannelCount];

    /// <summary>Read-only view of the current frame.</summary>
    public ReadOnlySpan<byte> Channels => _channels;

    /// <summary>
    /// Set one channel's value. DMX addresses are 1-based (1..512).
    /// </summary>
    public void Set(int address, byte value)
    {
        if (address < 1 || address > ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(address), "DMX address must be 1-512.");

        _channels[address - 1] = value;
    }

    /// <summary>Read one channel's current value.</summary>
    public byte Get(int address)
    {
        if (address < 1 || address > ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(address), "DMX address must be 1-512.");

        return _channels[address - 1];
    }

    /// <summary>Set every channel back to 0.</summary>
    public void Clear() => Array.Clear(_channels);

    /// <summary>
    /// Copy of the current frame, safe to hand to another thread (unlike
    /// the ReadOnlySpan from Channels, a plain array has no stack-only
    /// lifetime restriction).
    /// </summary>
    public byte[] Snapshot() => (byte[])_channels.Clone();
}
