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

    public override string ToString() => $"{Label} — {Profile.Name} @ {StartAddress}";
}
