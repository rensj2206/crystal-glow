using System.IO.Ports;

namespace CrystalGlow.Dmx;

/// <summary>
/// Sends DMX frames using the Enttec "DMX USB PRO" framed packet
/// protocol, rather than raw DMX512 bit-banging over the serial line.
/// A LOT of cheap USB-to-DMX dongles (including many CP210x-based ones
/// sold generically as "USB to DMX512 interface") are not a bare
/// USB-serial-to-RS485 wire at all -- they have their own onboard
/// microcontroller that handles the actual DMX512 timing (break, MAB,
/// 250kbit/s, continuous refresh) itself. The host never touches that
/// timing directly; it just sends the dongle a simple framed packet over
/// an ordinary serial connection, and the dongle's firmware takes it from
/// there. If that's what this dongle is, SerialDmxOutput's raw
/// bit-banging approach was never going to work, however precisely
/// tuned, because the device was never listening for raw DMX timing on
/// the wire -- it wanted packets.
/// Packet format (Enttec DMX USB PRO API, widely cloned):
///   [0x7E] [label] [length lo] [length hi] [data...] [0xE7]
/// Label 6 = "Output Only Send DMX Packet request". Data is the DMX
/// start code (0x00) followed by up to 512 channel values.
/// The serial baud rate used to reach the dongle over USB is NOT the
/// 250000 DMX baud -- that's irrelevant here since the dongle's firmware
/// is doing the real DMX timing on its own output; the host<->dongle
/// link just carries the framed packet. 57600 is the most commonly used
/// default across Enttec-protocol implementations and clones; change
/// DongleLinkBaudRate if your dongle needs a different one.
/// </summary>
public class EnttecProtocolDmxOutput : IDmxOutput
{
    private const byte PacketStart = 0x7E;
    private const byte PacketEnd = 0xE7;
    private const byte SendDmxPacketLabel = 6;

    /// <summary>
    /// Baud rate for the host<->dongle USB link itself (not DMX's
    /// 250000). 57600 is the common default for Enttec-protocol clones.
    /// </summary>
    private const int DongleLinkBaudRate = 57600;

    private readonly SerialPort _port;

    public EnttecProtocolDmxOutput(string portName)
    {
        _port = new SerialPort(portName, DongleLinkBaudRate, Parity.None, 8, StopBits.One);
        _port.Open();
    }

    public void Send(ReadOnlySpan<byte> channels)
    {
        if (!_port.IsOpen)
            return;

        // Data = start code (0x00) + channel values.
        var dataLength = channels.Length + 1;

        var packet = new byte[dataLength + 5];
        packet[0] = PacketStart;
        packet[1] = SendDmxPacketLabel;
        packet[2] = (byte)(dataLength & 0xFF);        // length, low byte
        packet[3] = (byte)((dataLength >> 8) & 0xFF); // length, high byte
        packet[4] = 0x00;                             // DMX start code
        channels.CopyTo(packet.AsSpan(5));
        packet[^1] = PacketEnd;

        _port.Write(packet, 0, packet.Length);
    }

    public void Dispose()
    {
        if (_port.IsOpen)
            _port.Close();

        _port.Dispose();
    }
}
