namespace CrystalGlow.Dmx;

/// <summary>
/// No-op output used when no DMX hardware is connected (or couldn't be
/// opened). Lets the rest of the app run normally -- sliders still edit
/// the DmxUniverse buffer, nothing just gets sent anywhere.
/// </summary>
public class NullDmxOutput : IDmxOutput
{
    public void Send(ReadOnlySpan<byte> channels) { }

    public void Dispose() { }
}
