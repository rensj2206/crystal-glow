using System.IO.Ports;
using System.Text;

// Usage: dotnet run --project src/CrystalGlow.Listener -- [COMPORT]
// Default port: COM7 (a guess -- whatever SECOND interface you plug in
// to listen will get its own port number; check Device Manager).
//
// WHAT THIS IS FOR
// -----------------
// This is the "listening post": instead of trusting that a signal we
// SEND actually arrived correctly (which otherwise only you, watching
// the lamp, can tell us), this reads whatever is actually present on a
// DMX line and reports it back directly -- no human-eyes-on-a-lamp step
// needed to know whether a byte-for-byte valid DMX512 signal is present.
//
// HARDWARE REQUIREMENT -- READ THIS FIRST
// -----------------------------------------
// This CANNOT read the same COM port CrystalGlow.Controller or
// CrystalGlow.Diagnostic is writing to -- a single USB-serial dongle is
// wired as a transmitter (or, on some, half-duplex, but not usefully
// "listen to my own output" for our purposes). To actually verify a
// signal, you need a SECOND DMX-capable interface, with its DMX IN wired
// into whatever is carrying our output (either chained after the
// fixture's DMX OUT, or splitting the line with a cheap DMX splitter/
// the fixture's pass-through port). Plug that second interface in, find
// its COM port in Device Manager, and point this at it.
//
// WHAT IT ACTUALLY DETECTS
// -------------------------
// A real DMX512 BREAK (the low pulse at the start of every frame) lands
// in the middle of what a UART expects to be a stop bit. Standard UART
// hardware reports that mismatch as a framing error -- so this listens
// for SerialError.Frame, which is the closest thing to directly
// "seeing" a break without needing an oscilloscope. Every such event
// logged here is a real break arriving on the wire, independent of
// anything this project's own code claims about what it sent.

var portName = args.Length > 0 ? args[0] : "COM7";

Console.WriteLine($"Opening {portName} at 250000 baud, 8N2, as a LISTENER (not sending anything)...");

SerialPort port;
try
{
    port = new SerialPort(portName, 250000, Parity.None, 8, StopBits.Two)
    {
        ReadBufferSize = 8192,
    };
    port.Open();
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED to open {portName}: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("Check Device Manager for the SECOND interface's port number.");
    return;
}

Console.WriteLine("Opened successfully. Listening...");
Console.WriteLine(Console.IsInputRedirected
    ? "Input is redirected -- stop this with Ctrl+C."
    : "Press any key to stop.");
Console.WriteLine();

var totalBytes = 0L;
var totalFramingErrors = 0L;
var lastEventTime = DateTime.UtcNow;
var lockObj = new object();

port.ErrorReceived += (_, e) =>
{
    lock (lockObj)
    {
        if (e.EventType == SerialError.Frame)
        {
            totalFramingErrors++;
            var now = DateTime.UtcNow;
            var gap = now - lastEventTime;
            lastEventTime = now;
            Console.WriteLine($"[BREAK?] Framing error (possible DMX break) -- {gap.TotalMilliseconds:F1}ms since last event. Total so far: {totalFramingErrors}");
        }
        else
        {
            Console.WriteLine($"[ERROR] {e.EventType}");
        }
    }
};

port.DataReceived += (_, e) =>
{
    lock (lockObj)
    {
        var available = port.BytesToRead;
        if (available <= 0)
            return;

        var buffer = new byte[available];
        var read = port.Read(buffer, 0, available);
        totalBytes += read;

        var now = DateTime.UtcNow;
        var gap = now - lastEventTime;
        lastEventTime = now;

        var previewCount = Math.Min(read, 16);
        var hex = string.Join(' ', buffer.Take(previewCount).Select(b => b.ToString("X2")));
        Console.WriteLine($"[DATA] {read,4} bytes ({gap.TotalMilliseconds,6:F1}ms gap)  first {previewCount}: {hex}{(read > previewCount ? " ..." : "")}");
    }
};

var summaryTimer = new System.Threading.Timer(_ =>
{
    lock (lockObj)
    {
        Console.WriteLine($"--- summary: {totalBytes} bytes, {totalFramingErrors} possible breaks so far ---");
    }
}, null, 5000, 5000);

while (Console.IsInputRedirected || !Console.KeyAvailable)
{
    Thread.Sleep(100);
}

summaryTimer.Dispose();
port.Close();
port.Dispose();

Console.WriteLine();
Console.WriteLine($"Stopped. Totals: {totalBytes} bytes received, {totalFramingErrors} possible breaks detected.");

if (totalFramingErrors == 0 && totalBytes == 0)
{
    Console.WriteLine("Nothing arrived at all -- check wiring to the second interface, and that something is actually sending.");
}
else if (totalFramingErrors == 0 && totalBytes > 0)
{
    Console.WriteLine("Bytes arrived but no framing errors (no detected breaks) -- this does NOT look like valid DMX512 framing.");
}
else
{
    Console.WriteLine("Breaks were detected -- this looks like genuine DMX512 framing is reaching this listener.");
}
