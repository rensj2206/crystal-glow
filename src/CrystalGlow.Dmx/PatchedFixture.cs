namespace CrystalGlow.Dmx;

/// <summary>
/// One fixture *instance* in your actual rig: a profile (channel layout)
/// patched to a specific DMX start address. This is the "patch list" entry
/// — the start address here must match whatever you set on the fixture's
/// own DIP switches/menu.
/// </summary>
public class PatchedFixture
{
    private readonly DmxUniverse _universe;

    public string Label { get; set; }
    public FixtureProfile Profile { get; }
    public int StartAddress { get; }

    public PatchedFixture(DmxUniverse universe, FixtureProfile profile, int startAddress, string label)
    {
        _universe = universe;
        Profile = profile;
        StartAddress = startAddress;
        Label = label;
    }

    public void SetChannel(int offset, byte value) => _universe.Set(StartAddress + offset, value);

    public byte GetChannel(int offset) => _universe.Get(StartAddress + offset);

    /// <summary>
    /// Write a 16-bit value across a coarse+fine channel pair (e.g. pan/tilt).
    /// coarseOffset gets the high byte, fineOffset gets the low byte.
    /// </summary>
    public void SetChannel16(int coarseOffset, int fineOffset, ushort value)
    {
        SetChannel(coarseOffset, (byte)(value >> 8));
        SetChannel(fineOffset, (byte)(value & 0xFF));
    }

    /// <summary>Read back a 16-bit value written with SetChannel16.</summary>
    public ushort GetChannel16(int coarseOffset, int fineOffset) =>
        (ushort)((GetChannel(coarseOffset) << 8) | GetChannel(fineOffset));

    public override string ToString() => $"{Label} — {Profile.Name} @ {StartAddress}";
}
