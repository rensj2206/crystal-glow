namespace CrystalGlow.Dmx;

/// <summary>
/// One named channel within a fixture profile, e.g. "Red" at offset 0.
/// Offset is relative to whatever start address a fixture is patched at.
/// </summary>
public class ChannelDefinition
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
}

/// <summary>
/// Describes a fixture *type* (its channel layout), not a specific instance
/// in your rig. Loaded from JSON so adding a new fixture model doesn't
/// require writing a new C# class — see FixtureProfileLoader.
/// </summary>
public class FixtureProfile
{
    public string Name { get; set; } = "";
    public List<ChannelDefinition> Channels { get; set; } = new();

    /// <summary>How many consecutive DMX addresses this fixture occupies.</summary>
    public int ChannelCount => Channels.Count == 0 ? 1 : Channels.Max(c => c.Offset) + 1;

    public override string ToString() => Name;
}
