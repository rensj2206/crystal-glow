using FTD2XX_NET;

namespace CrystalGlow.Dmx;

/// <summary>
/// Sends DMX frames out over a plain FTDI-chip "Open DMX" style USB
/// dongle (an FT232R or similar with an RS-485 transceiver, and no
/// onboard microcontroller -- the break/frame timing is done here in
/// software rather than by the device).
///
/// Requires the FTDI D2XX driver to be installed (not just the normal
/// VCP/serial driver) -- see https://ftdichip.com/drivers/d2xx-drivers/.
///
/// Break/MAB timing uses Thread.Sleep, which is not cycle-accurate on
/// Windows (~1ms+ granularity). That's fine here: DMX512 only requires a
/// MINIMUM break of 88us and MAB of 8us -- overshooting just caps the
/// achievable frame rate, it doesn't break the protocol.
/// </summary>
public class FtdiOpenDmxOutput : IDmxOutput
{
    private readonly FTDI _ftdi = new();
    private bool _isOpen;

    public FtdiOpenDmxOutput(int deviceIndex = 0)
    {
        var status = _ftdi.OpenByIndex((uint)deviceIndex);
        if (status != FTDI.FT_STATUS.FT_OK)
            throw new InvalidOperationException($"Could not open FTDI device {deviceIndex}: {status}");

        try
        {
            Check(_ftdi.SetBaudRate(250000), "SetBaudRate");
            Check(_ftdi.SetDataCharacteristics(
                FTDI.FT_DATA_BITS.FT_BITS_8,
                FTDI.FT_STOP_BITS.FT_STOP_BITS_2,
                FTDI.FT_PARITY.FT_PARITY_NONE), "SetDataCharacteristics");
            Check(_ftdi.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0), "SetFlowControl");
            Check(_ftdi.SetTimeouts(1000, 1000), "SetTimeouts");
        }
        catch
        {
            _ftdi.Close();
            throw;
        }

        _isOpen = true;
    }

    public void Send(ReadOnlySpan<byte> channels)
    {
        if (!_isOpen)
            return;

        // Break, then Mark-After-Break.
        _ftdi.SetBreak(true);
        Thread.Sleep(1);
        _ftdi.SetBreak(false);
        Thread.Sleep(1);

        // Start code (0x00 = standard dimmer data) followed by the channel bytes.
        var frame = new byte[channels.Length + 1];
        frame[0] = 0x00;
        channels.CopyTo(frame.AsSpan(1));

        uint written = 0;
        _ftdi.Write(frame, frame.Length, ref written);
    }

    public void Dispose()
    {
        if (_isOpen)
        {
            _ftdi.Close();
            _isOpen = false;
        }
    }

    private static void Check(FTDI.FT_STATUS status, string step)
    {
        if (status != FTDI.FT_STATUS.FT_OK)
            throw new InvalidOperationException($"FTDI {step} failed: {status}");
    }
}
