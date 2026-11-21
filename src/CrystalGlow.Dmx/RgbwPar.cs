namespace CrystalGlow.Dmx;

/// <summary>
/// A simple 4-channel RGBW par fixture: offsets 0-3 from its start address
/// map to Red, Green, Blue, White. Adjust the offsets/channel count to
/// match whatever fixture you actually have — this is just an example shape.
/// </summary>
public class RgbwPar
{
    private readonly DmxUniverse _universe;
    public int StartAddress { get; }

    public RgbwPar(DmxUniverse universe, int startAddress)
    {
        _universe = universe;
        StartAddress = startAddress;
    }

    public byte Red
    {
        get => _universe.Get(StartAddress + 0);
        set => _universe.Set(StartAddress + 0, value);
    }

    public byte Green
    {
        get => _universe.Get(StartAddress + 1);
        set => _universe.Set(StartAddress + 1, value);
    }

    public byte Blue
    {
        get => _universe.Get(StartAddress + 2);
        set => _universe.Set(StartAddress + 2, value);
    }

    public byte White
    {
        get => _universe.Get(StartAddress + 3);
        set => _universe.Set(StartAddress + 3, value);
    }

    public void SetColor(byte r, byte g, byte b)
    {
        Red = r;
        Green = g;
        Blue = b;
    }
}
