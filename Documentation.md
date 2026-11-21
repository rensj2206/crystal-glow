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