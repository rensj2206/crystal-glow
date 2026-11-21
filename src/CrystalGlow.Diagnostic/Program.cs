using CrystalGlow.Dmx;

// Usage: dotnet run --project src/CrystalGlow.Diagnostic -- [COMPORT] [ADDRESS] [TRANSPORT]
// Defaults: COM6, address 1, transport "raw".
// TRANSPORT is "raw" (bit-banged Open-DMX style) or "enttec" (framed packet protocol).
//
// This bypasses the WPF app, the patch system, and fixture profiles
// entirely. It just blinks DMX channel "address" (Red, if your fixture
// is patched at this address) between 0 and 255 every 2 seconds, with
// every step logged, so we can tell definitively whether ANY valid DMX
// signal is reaching the fixture -- independent of everything else in
// the project.
//
// Put the fixture in real DMX mode first (switches 8, 9, 10 all OFF),
// with its own DIP-switch address matching the address given here.
//
// Two transports are available because which one this dongle actually
// speaks is unverified -- try both:
//   raw     -- host bit-bangs real DMX512 timing (SerialDmxOutput)
//   enttec  -- dongle has its own microcontroller, host just sends a
//              framed packet (EnttecProtocolDmxOutput)

var portName = args.Length > 0 ? args[0] : "COM6";
var address = args.Length > 1 ? int.Parse(args[1]) : 1;
var transportArg = args.Length > 2 ? args[2].ToLowerInvariant() : "raw";

IDmxOutput output;

Console.WriteLine($"Opening {portName}, transport={transportArg}...");

try
{
    output = transportArg switch
    {
        "enttec" => new EnttecProtocolDmxOutput(portName),
        "raw" => new SerialDmxOutput(portName),
        _ => throw new ArgumentException($"Unknown transport '{transportArg}'. Use 'raw' or 'enttec'."),
    };
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED to open {portName}: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("Check Device Manager -- the COM port number may have changed.");
    return;
}

Console.WriteLine("Opened successfully.");
Console.WriteLine();
Console.WriteLine($"Blinking channel at DMX address {address} between 0 and 255 every 2 seconds.");
Console.WriteLine("Fixture must be in real DMX mode (switches 8, 9, 10 all OFF) and patched");
Console.WriteLine($"at address {address} to see anything.");
Console.WriteLine(Console.IsInputRedirected
    ? "Input is redirected -- stop this with Ctrl+C instead of a keypress."
    : "Press any key to stop.");
Console.WriteLine();

var universe = new byte[512];
var on = false;
var frame = 0;

// Console.KeyAvailable throws if stdin has been redirected (e.g. when
// launched from some non-interactive contexts) -- only poll it when
// input is actually an interactive console.
while (Console.IsInputRedirected || !Console.KeyAvailable)
{
    on = !on;
    frame++;
    universe[address - 1] = (byte)(on ? 255 : 0);

    try
    {
        output.Send(universe);
        Console.WriteLine($"[{frame}] Sent value={(on ? 255 : 0)} at address {address}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{frame}] SEND FAILED: {ex.GetType().Name}: {ex.Message}");
    }

    Thread.Sleep(2000);
}

output.Dispose();
Console.WriteLine("Stopped.");
