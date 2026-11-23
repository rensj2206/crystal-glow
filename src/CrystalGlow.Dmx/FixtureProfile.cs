namespace CrystalGlow.Dmx;

/// <summary>
/// One named, discrete setting within a channel's range of values, e.g.
/// "Red" covering DMX values 31-45 on a colour-preset channel. Used for
/// channels that are really a fixed menu of choices rather than a
/// continuous value (show modes, colour macros, etc.) — see
/// ChannelDefinition.Options.
/// </summary>
public class ChannelOption
{
    public string Name { get; set; } = "";
    public int Min { get; set; }
    public int Max { get; set; }

    /// <summary>The single DMX value actually sent when this option is selected.</summary>
    public int RepresentativeValue => (Min + Max) / 2;

    public bool Contains(int value) => value >= Min && value <= Max;
}

/// <summary>
/// One named channel within a fixture profile, e.g. "Red" at offset 0.
/// Offset is relative to whatever start address a fixture is patched at.
///
/// Some channels (typically pan/tilt on moving heads) are 16-bit: a
/// coarse byte plus a separate "fine" byte elsewhere in the footprint,
/// combined for finer resolution than a single 0-255 byte allows. Set
/// FineOffset for those; leave it null for a plain 8-bit channel.
///
/// Some channels are really a fixed menu of named settings rather than
/// a continuous value (show modes, colour macros). Set Options for
/// those -- the UI shows a snapping/"ratchet" slider over the named
/// options instead of a plain 0-255 slider.
/// </summary>
public class ChannelDefinition
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
    public int? FineOffset { get; set; }
    public List<ChannelOption>? Options { get; set; }

    public bool Is16Bit => FineOffset.HasValue;
    public bool HasOptions => Options is { Count: > 0 };

    /// <summary>Index into Options whose range contains the given raw DMX value, or 0 if none match.</summary>
    public int OptionIndexForValue(int rawValue)
    {
        if (Options is null)
            return 0;

        for (var i = 0; i < Options.Count; i++)
        {
            if (Options[i].Contains(rawValue))
                return i;
        }

        return 0;
    }
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
    public int ChannelCount => Channels.Count == 0
        ? 1
        : Channels.Max(c => Math.Max(c.Offset, c.FineOffset ?? c.Offset)) + 1;

    public override string ToString() => Name;
}
