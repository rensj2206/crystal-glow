using CrystalGlow.Dmx;

// Two usage forms:
//
// 1) Blink mode (unchanged): dotnet run --project src/CrystalGlow.Diagnostic -- [COMPORT] [ADDRESS] [TRANSPORT]
//    Defaults: COM6, address 1, transport "raw". Blinks one channel 0/255 every 2 seconds.
//
// 2) Set mode (new): dotnet run --project src/CrystalGlow.Diagnostic -- [COMPORT] set [TRANSPORT] addr:value [addr:value ...]
//    Sends a fixed set of channel values continuously (holds them, doesn't blink) --
//    for setting several channels at once, e.g. multiple heads' pan/tilt in one go.
//    Example: dotnet run --project src/CrystalGlow.Diagnostic -- COM6 set raw 7:30 8:200 10:90 11:10
//
// TRANSPORT is "raw" (bit-banged Open-DMX style) or "enttec" (framed packet protocol).
//
// This bypasses the WPF app, the patch system, and fixture profiles
// entirely -- direct, logged, address-level control for verifying real
// signal reaches the fixture, independent of everything else in the
// project.
//
// Put the fixture in real DMX mode first (switches 8, 9, 10 all OFF),
// with its own start address matching the addresses given here.

var portName = args.Length > 0 ? args[0] : "COM6";
var isSetMode = args.Length > 1 && args[1].Equals("set", StringComparison.OrdinalIgnoreCase);

string transportArg;
var universe = new byte[512];
var isBlinkMode = !isSetMode;
var blinkAddress = 1;

if (isSetMode)
{
    transportArg = args.Length > 2 ? args[2].ToLowerInvariant() : "raw";

    var pairs = args.Skip(3).ToList();
    if (pairs.Count == 0)
    {
        Console.WriteLine("Set mode needs at least one addr:value pair, e.g. 7:30 8:200");
        return;
    }

    foreach (var pair in pairs)
    {
        var parts = pair.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var addr) || !int.TryParse(parts[1], out var val)
            || addr < 1 || addr > 512 || val < 0 || val > 255)
        {
            Console.WriteLine($"Bad addr:value pair '{pair}' -- expected e.g. '7:30' (address 1-512, value 0-255).");
            return;
        }

        universe[addr - 1] = (byte)val;
    }
}
else
{
    blinkAddress = args.Length > 1 ? int.Parse(args[1]) : 1;
    transportArg = args.Length > 2 ? args[2].ToLowerInvariant() : "raw";
}

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

if (isSetMode)
{
    Console.WriteLine("Holding these channel values continuously:");
    for (var i = 0; i < universe.Length; i++)
    {
        if (universe[i] != 0)
            Console.WriteLine($"  address {i + 1,3} = {universe[i]}");
    }
}
else
{
    Console.WriteLine($"Blinking channel at DMX address {blinkAddress} between 0 and 255 every 2 seconds.");
}

Console.WriteLine("Fixture must be in real DMX mode (switches 8, 9, 10 all OFF) with a matching start address.");
Console.WriteLine(Console.IsInputRedirected
    ? "Input is redirected -- stop this with Ctrl+C instead of a keypress."
    : "Press any key to stop.");
Console.WriteLine();

var on = false;
var frame = 0;

// Console.KeyAvailable throws if stdin has been redirected (e.g. when
// launched from some non-interactive contexts) -- only poll it when
// input is actually an interactive console.
while (Console.IsInputRedirected || !Console.KeyAvailable)
{
    frame++;

    if (isBlinkMode)
    {
        on = !on;
        universe[blinkAddress - 1] = (byte)(on ? 255 : 0);
    }

    try
    {
        output.Send(universe);
        if (isBlinkMode)
            Console.WriteLine($"[{frame}] Sent value={(on ? 255 : 0)} at address {blinkAddress}");
        else if (frame == 1 || frame % 10 == 0)
            Console.WriteLine($"[{frame}] Still holding...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{frame}] SEND FAILED: {ex.GetType().Name}: {ex.Message}");
    }

    // Set mode refreshes faster (continuous hold, like the real transmit
    // loop) since there's nothing to visually "blink" -- just needs to
    // keep sending so the fixture doesn't time out.
    Thread.Sleep(isBlinkMode ? 2000 : 200);
}

output.Dispose();
Console.WriteLine("Stopped.");
