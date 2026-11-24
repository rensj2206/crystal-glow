Crystal Glow Docomentation
===============

### Introduction, what is Crystal Glow?

Crystal Glow is a flexible and simple DMX controller that can be used to control lighting fixtures in various settings. 
It is designed for both beginners and professionals, providing an intuitive interface and powerful features. 
It includes support for sequences, effects, beat matched lighting and more. 

### Day 2 - Wednesday 18/10/2026

The first day was spent setting up our pitch deck, trello board, and our powerpoint presentation. 
No development of the code took place on Day 1

Nothing uploaded to GitHub

### Day 3 - Thursday 19/10/2026

The first day of development focused on setting up the basic architecture of the Crystal Glow controller. 
The program had issues to begin with, such as the DMX output not functioning correctly and the user interface being unresponsive. 

We first assumed it was a simple bug in the code, but after further investigation, we realized that the issue was more complex. 
We then tried rewriting the DMX output code, installing new drivers, changing adapter cables, and even testing on switching out the lamp as a whole to see if it was a hardware problem, but nothing has worked so far 

Nothing uploaded to GitHub

### Day 4 - Friday 20/10/2026

No work done on this day 

Nothing uploaded to GitHub

### Day 5 - Satuday 21/10/2026

After careful thought, the program as it was has been scrapped. No changes could be made to the lightshow at all, so a new approach has been taken. Made blindly without access to any of the DMX lights, so it remains untested as of now. 

To clarify what that actually involved: our teacher pointed out that RS-485 (the electrical signal on the DMX
cable) and DMX512 (the data framing sent over it) are two separate protocols, and we had only really been dealing
with one of them. Two things were missing:

- The dongle's RS-485 line driver needs its enable pin asserted (through the serial port's RTS/DTR lines), or the
  USB chip happily sends bytes internally (so its activity light blinks) while nothing actually reaches the DMX
  line.
- Every DMX frame needs a real BREAK signal for the fixture to sync to. The normal way of generating one didn't
  reliably work on our adapter, so instead we generate it by briefly dropping to a much lower baud rate and sending
  a single zero byte, since the byte's own start bit and zero data bits create a long enough low pulse to work as a
  break on their own.

With both of those fixed, we got our first real sign of life: a small indicator LED on the back of the fixture
(separate from the main light) started blinking in time with our test signal, confirming a valid DMX signal was
finally reaching it, correctly addressed. The main light itself still didn't respond, though.

Our current best guess is that a lot of cheap USB-to-DMX adapters like ours don't expect raw DMX timing from the
computer at all -- they have their own onboard chip that handles the real DMX timing itself, and just want a simple
wrapped data packet sent to them instead (a widely cloned protocol originally from Enttec's "DMX USB PRO"
adapters). Since we couldn't test either theory against the real lights while writing this, we built both the "raw
signal" approach and the "wrapped packet" approach side by side, switchable instantly from a dropdown in the app,
so whichever one is correct can be tried immediately once we have the hardware back, without more guessing and
rebuilding blind.

Until then, this remains untested

This is the current version on GitHub 

### Day 6 - Sunday 22/10/2026 

No work done today 

### Day 7 - Monday 23/10/2026 

Working prototype accomplished on the Hydrabeam 400s
The ShowTecs are still are gamble of sorts. Sometimes they work, sometimes they don't. The DMX signal is being sent to them, 
but they don't always respond, and can at times switch to their automatic pre-programmed lightshow. 
Hydrabeams so far have only been tested in their 6ch mode for simplicities sake, but we soon will begin testing their 56ch mode.

### Day 8 - Tuesday 24/10/2026

Full day of testing and fixing on the Hydrabeam side.

**Accurate channel data.** Our earlier Hydrabeam profiles were guessed from a manual for the "Hydrabeam 4000" (a
sibling fixture), not our actual "Hydrabeam 400". We found the real fixture on the Open Fixture Library
(open-fixture-library.org) and rebuilt all five of its channel modes (6ch, 10ch, 19ch, 32ch, 56ch) from that data
instead, channel-for-channel.

**16-bit pan/tilt support added.** Smooth, precise pan/tilt needs a "coarse" and "fine" byte combined into one
16-bit value, which our channel model didn't support until now. Fixture profiles can now mark a channel as a
coarse+fine pair, and the app shows a wider slider (0-65535) for those instead of a plain 0-255 one.

**Ratchet/snapping sliders added.** Several channels (Show Mode, Light Control, Colour presets, Sound Mode/Reset)
aren't really a continuous value, they're a fixed menu of named settings. Instead of a plain slider where most
positions do the same thing, those channels now show a slider that snaps to each named option and displays its
name (e.g. "Show mode 4", "Red + Blue") instead of a raw number.

**The 32ch/56ch "doesn't work" scare, and what it actually was.** After getting 6ch, 10ch and 19ch mode working, we
tested 32ch and 56ch next and got nothing -- no movement, no light, on either mode, even after power-cycling the
fixture and triple-checking the menu was set to match. We even tried an established program (QLC+) as an
independent check, which also showed nothing, and our conclusion at the time was that this specific fixture's
firmware probably didn't really support those higher modes, despite listing them as menu options.

That conclusion was wrong, and we found out why. Two things, both revealing the same lesson:

1. QLC+'s result wasn't meaningful at all -- its DMX output plugin is built around FTDI-chip hardware
   specifically, and our adapter uses a completely different chip (Silicon Labs), so QLC+ was never actually
   talking to our adapter in the first place, in any mode. That test told us nothing.
2. The real reason 32ch/56ch looked broken: in those modes, a head's actual visible output depends on *several*
   channels being set together -- the dimmer needs to be non-zero, the colour channel needs to be pointed at
   either a direct preset or the separate RGBW channels, and if it's pointed at RGBW, those need values too. Our
   tests up to that point had only ever changed *one* channel at a time (just pan, or just colour) while
   everything else sat at its default of zero -- which looks identical to "nothing is happening" even when the
   signal and addressing are both completely correct. We'd actually run into this exact trap once already, on
   19ch mode's per-head dimmer channel, and didn't realise it would scale up to the bigger modes too.

Once we set every relevant channel at once instead of one at a time, 32ch and 56ch both worked properly -- all 4
heads moving independently on both pan and tilt, each showing a different colour, in a single test. All five
channel modes are now confirmed working on the real hardware, not just assumed from a datasheet.

**New tooling.** The command-line diagnostic tool gained a "set" mode that holds several channels at fixed values
at once (instead of only blinking one channel), which is what let us catch and fix the issue above -- useful
beyond this one bug too, for any future test that needs more than a single value changing at a time.

### Day 8 (continued)

A few more things rounded out the day after the 32ch/56ch fix above:

**Remove Fixture button added.** The patch list only ever had an "Add" button, with no way to un-patch something.
This bit us directly: after testing several Hydrabeam channel modes one after another, old patch entries from
earlier modes were still sitting in the list, all still writing to the universe at the same time as the new one --
so moving a slider that the app labelled "Colour" on the new mode could actually be landing on a completely
different channel (like pan) left over from a stale patch at an overlapping address. Added a proper "Remove
Selected Fixture" button, which also zeroes that fixture's channel range on the way out so old values don't keep
going out on the wire.

**Scrolling added to the channel panel.** With 56 channels in 56ch mode, the channel list ran off the bottom of
the window with no way to reach the rest. Wrapped it in a scroll view, keeping the fixture name pinned at the top.

**19ch mode fully confirmed.** Same "set multiple channels together" lesson as 32ch/56ch applied here too --
getting any head to actually show light needs the master Dimmer, a master colour channel (Red/Green/Blue/White),
and that head's own individual Dimmer all set at once. Confirmed working once we did that.

**Final result: all five Hydrabeam channel modes (6ch, 10ch, 19ch, 32ch, 56ch) are now fully working** -- movement
and light/colour both -- on the real hardware. The dependency between channels (several values needing to be set
together before anything shows) is just how this fixture's real DMX layout works, not something we can remove in
software, but it's now well understood and no longer a mystery.

