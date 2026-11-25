using CrystalGlow.Dmx;

// Usage: dotnet run --project src/CrystalGlow.SoundReactive -- [COMPORT] [ADDRESS]
//
// Console front-end for SoundReactiveEngine (see CrystalGlow.Dmx) -- live
// "sound to light" via WASAPI loopback capture. With no arguments, just
// prints a live bar graph so you can confirm it's reacting to whatever's
// playing before wiring it to real hardware. With COMPORT and ADDRESS
// given, also drives that DMX channel live (e.g. a fixture's dimmer).

var comPort = args.Length > 0 ? args[0] : null;
var address = args.Length > 1 ? int.Parse(args[1]) : 1;

DmxTransmitLoop? transmitLoop = null;
DmxUniverse? universe = null;

if (comPort != null)
{
    try
    {
        var output = new SerialDmxOutput(comPort);
        universe = new DmxUniverse();
        transmitLoop = new DmxTransmitLoop(universe, output);
        transmitLoop.SendFailed += ex => Console.WriteLine($"[DMX SEND FAILED] {ex.Message}");
        transmitLoop.Start();
        Console.WriteLine($"Driving DMX address {address} on {comPort} live.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not open {comPort}: {ex.Message}. Continuing in analyze-only mode.");
        comPort = null;
    }
}
else
{
    Console.WriteLine("No COM port given -- analyze-only mode (prints a live bar graph, no DMX output).");
}

var engine = new SoundReactiveEngine();

var lastPrint = DateTime.UtcNow;
var printInterval = TimeSpan.FromMilliseconds(100);

engine.LevelChanged += intensity =>
{
    universe?.Set(address, intensity);

    var now = DateTime.UtcNow;
    if (now - lastPrint < printInterval)
        return;

    lastPrint = now;
    var barLength = intensity * 50 / 255;
    Console.Write($"\r[{new string('#', barLength),-50}] {intensity,3}  ");
};

engine.Start();
Console.WriteLine("Capturing system audio. Play some music now. Press any key to stop.");
Console.WriteLine();

while (Console.IsInputRedirected || !Console.KeyAvailable)
    Thread.Sleep(50);

engine.Dispose();
transmitLoop?.Dispose();
Console.WriteLine();
Console.WriteLine("Stopped.");
