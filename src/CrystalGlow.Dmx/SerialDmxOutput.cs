using System.IO.Ports;

namespace CrystalGlow.Dmx;

/// <summary>
/// Sends DMX frames out over a plain USB-to-serial dongle that Windows
/// recognizes as a normal COM port -- e.g. a Silicon Labs CP210x bridge.
/// Unlike FtdiOpenDmxOutput, this needs no vendor-specific driver.
///
/// Two separate protocol layers both have to be handled, not just one:
///
/// 1. RS-485 (the electrical transport). Some cheap USB-to-RS485 dongles
///    wire the RS-485 driver's enable pin to RTS and/or DTR. If neither
///    is asserted, the USB-serial chip still happily transmits bytes
///    internally (so its activity LED blinks) but the RS-485 output
///    stage itself stays disabled, and nothing reaches the DMX line.
///
/// 2. DMX512 (the framing on top of that transport). Every frame needs a
///    real BREAK the receiver can sync to. SerialPort.BreakState (=
///    SetCommBreak) isn't reliably honored by every USB-serial chip's
///    driver -- notably some CP210x-based ones. So instead of relying on
///    that, the break is produced purely through ordinary byte-
///    transmission timing: drop to a much lower baud rate and send a
///    single 0x00 byte. At that baud, the byte's start bit plus its 8
///    zero data bits create a continuous low period well over DMX's
///    minimum ~92us break requirement, and the stop bit(s) that follow
///    give the mark-after-break (min ~12us). This only depends on the
///    chip correctly sending a byte at a configured baud rate, which
///    every USB-serial chip does reliably.
/// </summary>
public class SerialDmxOutput : IDmxOutput
{
    private const int DataBaudRate = 250000;

    /// <summary>
    /// Baud rate used only to transmit the single break byte. At 50000
    /// baud, one bit period is 20us, so the break byte's low period
    /// (1 start bit + 8 zero data bits = 9 bit periods) is 180us --
    /// comfortably over the ~92us minimum.
    /// </summary>
    private const int BreakBaudRate = 50000;

    private static readonly byte[] BreakByte = { 0x00 };

    private readonly SerialPort _port;

    public bool RtsEnable => _port.RtsEnable;
    public bool DtrEnable => _port.DtrEnable;

    public SerialDmxOutput(string portName)
    {
        _port = new SerialPort(portName, DataBaudRate, Parity.None, 8, StopBits.Two);
        _port.Open();

        // RS-485 layer: make sure the dongle's line driver is actually enabled.
        _port.RtsEnable = true;
        _port.DtrEnable = true;
    }

    public void Send(ReadOnlySpan<byte> channels)
    {
        if (!_port.IsOpen)
            return;

        // DMX512 layer: generate a real BREAK via baud-rate timing rather
        // than trusting the driver's BreakState support.
        _port.BaudRate = BreakBaudRate;
        _port.Write(BreakByte, 0, 1);

        // Write() only queues the byte -- it does NOT wait for it to
        // actually leave the port. Switching the baud rate back before
        // that one byte has really been transmitted would apply the new
        // (fast) baud to it, destroying the break entirely while still
        // "succeeding" with no exception. Poll BytesToWrite instead of a
        // flat sleep, since USB-serial driver latency varies by chip.
        WaitForQueueToDrain();
        Thread.Sleep(1); // extra margin past the OS handing it to the chip

        _port.BaudRate = DataBaudRate;

        // Start code (0x00 = standard dimmer data) followed by the channel bytes.
        var frame = new byte[channels.Length + 1];
        frame[0] = 0x00;
        channels.CopyTo(frame.AsSpan(1));

        _port.Write(frame, 0, frame.Length);
    }

    public void Dispose()
    {
        if (_port.IsOpen)
            _port.Close();

        _port.Dispose();
    }

    private void WaitForQueueToDrain(int timeoutMs = 50)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_port.BytesToWrite > 0 && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(0);
        }
    }
}
