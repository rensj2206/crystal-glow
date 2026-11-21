using CrystalGlow.Dmx;

var universe = new DmxUniverse();

// One RGBW par starting at DMX address 1 (channels 1-4).
var par1 = new RgbwPar(universe, startAddress: 1);
par1.SetColor(r: 255, g: 0, b: 128);
par1.White = 32;

Console.WriteLine("First 8 channels of the universe:");
for (int address = 1; address <= 8; address++)
{
    Console.WriteLine($"  Ch {address,3}: {universe.Get(address)}");
}

// This is where sending the buffer to real hardware would go later —
// a USB-DMX dongle (FTDI/Open DMX), Art-Net, or sACN output.
Console.WriteLine();
Console.WriteLine("(No hardware output wired up yet — this just shows the buffer.)");
