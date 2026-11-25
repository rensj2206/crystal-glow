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

### Day 9 - Wednesday 25/10/2026

**Sound-reactive lighting, first working version.** We'd planned to let people upload a song and pre-analyse it
for bass peaks, but talked through the alternative of just listening to whatever's actually playing on the PC
live, the same way a real RTA/sound-to-light box would. That's simpler (no file format handling, works with any
source -- Spotify, a DJ app, a YouTube video) and more honest to how the hardware would actually be used, so we
built that instead.

Captures system audio via WASAPI loopback (NAudio) -- this listens to whatever the PC's output device is playing,
not a microphone and not an uploaded file. The incoming audio is run through a simple low-pass filter to isolate
bass, then an envelope follower (fast attack, slow release) turns that into a "flash then fade" level rather than
a jittery raw signal, and that level is normalised against a slowly-decaying recent peak so it doesn't need manual
sensitivity tuning per song. The DSP logic lives in `SoundReactiveEngine` in the shared `CrystalGlow.Dmx` project
so both the console prototype and the main app use the same code.

**UI added to the main app.** A new "Sound Reactive" panel on the patch screen lets you pick any channel on any
currently patched fixture as the target, then Start/Stop the engine. A live level meter shows the computed
intensity in real time, and while running, that intensity is written straight into the selected channel every
time it updates (supports both 8-bit and 16-bit channels).

**Confirmed on real hardware**: patched a fixture, picked its dimmer channel as the sound-reactive target,
played music with bass, and the fixture actually flashed in time with it. First real feature beyond manual
sliders, and the swappable-transport/profile-driven architecture from the "blind rebuild" handled a brand new
use case without needing changes to the DMX layer at all.

**Sensitivity control added.** The sound-reactive engine now has a `Sensitivity` multiplier (default 1.0,
adjustable 0.2x-3x live, no need to stop/restart) applied after the adaptive-peak normalisation step and before
clamping to 0-255. Raising it makes quieter bass register as a stronger flash (at the cost of loud bass clipping
to full intensity sooner); lowering it requires louder bass before the light reacts strongly. Exposed as a
slider in the Sound Reactive panel.

**Keybinds added for faster testing.** 1-9 selects a patched fixture from the list, Space starts/stops sound
reactive, +/- nudges sensitivity, Delete removes the selected fixture, N advances to the next sequence step, and
P toggles sequence play/pause. All ignored while a text box has focus so typing a label or address still works
normally. A short legend is shown in the UI itself.

**Sequences (first version).** Added a basic cue/scene sequencer: a "step" captures every channel's current
value on whichever fixture is selected in the patch list, plus a fade time (0 = sharp cut, otherwise fades
linearly over that many seconds) and a hold time (how long auto-play waits on that step before advancing).
Steps can target different fixtures from each other -- a sequence isn't locked to one fixture. Playback can be
driven manually (Next Step button / N key) or left to auto-play (Play button / P key), and fades always
interpolate from the fixture's *actual current* channel values rather than a stored "previous step" value, so
jumping to another step mid-fade still looks continuous instead of snapping backwards first. This lives in a new
shared `SequencePlayer`/`SequenceStep` pair in `CrystalGlow.Dmx`, running its own ~40Hz loop independent of the
UI thread (same cadence as the existing DMX transmit loop). Not yet tested against real hardware for fade
smoothness -- that's the next thing to verify.

**Confirmed working on real hardware**, sound-reactive flash included.

**Head linking added, then refined twice based on feedback.** Hydrabeam profiles (10ch/19ch/32ch/56ch) repeat
the same channel 4 times, once per head -- "Head 1 Pan", "Head 2 Pan", "Head 3 Pan", "Head 4 Pan", and likewise
for Tilt/Dimmer/Colour -- which meant moving all 4 heads together needed 4 separate slider drags every time.

First version was a single all-or-nothing "Link heads" checkbox per fixture. Feedback: linking needed to work on
arbitrary subsets too -- just a pair, or 3 of the 4 -- not only "all heads or none". Changed to one Link checkbox
per head number (built dynamically from whatever head numbers the profile actually has), so moving any
"Head N &lt;X&gt;" slider mirrors onto only the other *checked* heads; unchecked heads stay independent.

Second refinement: a plain mirror isn't always what's wanted -- e.g. two heads panning in opposite directions
(one clockwise, one counter-clockwise) rather than identically. Added an "inv" checkbox next to each head's Link
checkbox. When dragging a slider, the value is first normalized against whether the head being dragged is itself
inverted, then driven out to each other linked head either as-is or flipped to the opposite end of that channel's
range depending on whether *that* head is inverted. So: two linked heads both normal (or both inverted) mirror
exactly; one normal + one inverted move oppositely; this generalizes to any mix across a linked group of 2, 3, or
4 heads.

Matching is done by stripping the "Head N " prefix and comparing the rest of the channel name, so a channel only
ever links to its own counterparts (Pan only ever mirrors/inverts against other Pans, never Tilt or Dimmer).
Link/invert state is remembered per fixture, so switching between patched fixtures in the list keeps each one's
own setup. Has no effect on fixtures without repeated "Head N" channels (the Showtec par, for instance) -- no
controls are even shown for those.

**First custom-rendered control: a rotary knob for ratchet channels.** Started moving the UI off default plain
WPF controls, beginning with a real design: a knurled rotary knob and a brushed-metal fader cap, modelled and
rendered (top-down, transparent background) rather than drawn in code. Converted both from the rendered .webp
to .png (via WPF's own WIC-based decoder, run once through a throwaway PowerShell snippet -- no extra tools
needed) and added them under a new `Assets/` folder in the Controller project.

Built a new `Knob` control (`Controls/Knob.xaml` + code-behind) using the knob render: drag up/down to turn it,
rotating the image via a `RotateTransform` across a fixed -135° to +135° arc. It's a ratchet by design, same
as the snapping sliders it replaces -- there's no continuous mode, since an options-based DMX channel (colour
macros, show modes, etc.) is always a fixed menu of named settings, never a smooth range, so swapped it in for
every `HasOptions` channel in the main channel panel (Colour channels on 32ch/56ch Hydrabeam modes, for
instance) in place of the old snapping Slider.

Doing this meant generalizing how head-linking reads/writes a channel's value -- it used to grab `Slider.Value`
directly, which the new Knob doesn't have. Added a small `LinkableControl` adapter (get/set delegates + a range)
so linking works identically whether a channel is drawn as a Slider or a Knob, without either control needing to
know about the other.

Not yet tested against real hardware -- next step is confirming the knob actually turns smoothly and lands on
the right DMX value when dragged, and that it looks right against the rest of the UI (still plain sliders
everywhere else for now; the fader cap render hasn't been wired in yet).

**Transparency bug hunt.** The knob showed up in the app as a solid black diamond instead of a circular knob --
looked like a background-removal failure. Traced it with a quick pixel check (decode the file, read a corner
pixel's actual bytes) rather than guessing from how it looked: the very first renders had no alpha channel at
all (`Bgr32`, solid black fill), which meant the webp export itself never had transparency, regardless of any
Blender setting. After enabling Film -> Transparent in Blender and re-exporting, a second check *still* showed no
alpha -- but by then the files were coming into this conversation as pasted chat images, and it turned out that
path (not Blender) was the actual problem: pasting an image into chat re-encodes it as .webp for preview, which
flattened the transparency regardless of what Blender had actually produced. Checking the real files directly
from disk (`Knob.png`, `Dial.png`, `Button.Paused.png`, `Button.Playing.png`) confirmed all four were correctly
`Bgra32` with real transparency -- Blender's output was fine the whole time. Lesson: when checking a rendered
asset for transparency, check the actual file on disk, not a copy that's passed through a chat/preview pipeline.

Also sorted out a naming mismatch between the Blender project and the WPF assets: on disk, `Knob.png` is
actually the fader cap (rectangle + circle), and `Dial.png` is the rotary gear knob -- renamed/copied into
`Assets/` the right way round (`Assets/Knob.png` = the gear dial, `Assets/FaderCap.png` = the rectangle cap) so
the control code didn't need to change.

Two new button-state renders also came in -- `ButtonPaused.png` (shows a play triangle, i.e. what you see while
paused) and `ButtonPlaying.png` (shows a pause icon, i.e. what you see while playing) -- added to `Assets/` but
not wired into any control yet.

**Fader cap wired into every plain/16-bit slider.** Added three resources to `App.xaml`: a transparent
RepeatButton style (so the track still responds to click-to-jump without drawing its own visual), a Thumb style
that swaps in the rendered `FaderCap.png` as the draggable handle, and a `FaderSliderStyle` that replaces the
whole default Slider look with a thin dark groove plus that custom thumb. Applied it to every channel slider
that isn't a ratchet Knob (the plain 0-255 and 16-bit 0-65535 sliders), plus the sensitivity slider in the Sound
Reactive panel. The ratchet (options-based) channels keep using the Knob control from before -- sliders and
knobs are now visually distinct from each other, matching what they actually do.

