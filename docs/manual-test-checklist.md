# Manual test checklist

Run before publishing a release, with the exact `DhcpServer.exe` from the draft release, on at
least **Windows 7 SP1** (with .NET Framework 4.8) and **Windows 11**. Record date, OS edition and
bitness, and pass/fail per step in the release text.

Features add their own steps here (from their `quickstart.md`).

## Start-up and environment

1. Copy `DhcpServer.exe` (and `.exe.config`) to an empty folder, e.g. on a USB stick. Start it.
   The main window opens; no installer, no extra files except `DhcpServer.ini` after a setting
   change.
2. The UI language follows the OS language when supported, otherwise English.
3. Switch every language in **Language**. All texts change at once; nothing is clipped
   (German, French) and Japanese renders correctly.
4. Restart: the chosen language is kept.
5. Set the display to 150% scaling (log off/on on Windows 7). Text is sharp, nothing overlaps.
6. **Help → About** shows the release version.
7. Make the folder read-only and change the language: the program keeps working.

## DHCP lease server (feature 001)

Setup: a second PC or a device set to "obtain an IP address automatically", connected to the test PC
by cable (or a USB Ethernet adapter), and the test PC's adapter set to a static address such as
`192.168.137.1/24`. Accept the UAC prompt and the Windows Firewall prompt (allow the network type
shown; directly connected adapters are often classified *Public*).

8. **Happy path.** Pick the cable interface and press **Start**. The status shows the running address.
   Connect or reboot the device: it gets `192.168.137.2` or higher, and `ping` works both ways.
9. **Lease list.** The device appears within a second with IP, MAC and host name. `ipconfig /renew`
   on the other PC: no duplicate row. `ipconfig /release`: the row becomes *Released*. Press Ctrl+C
   on a row (IP and MAC are on the clipboard) and use the context menu *Copy IP address* / *Copy MAC address*.
10. **Persistence.** Press **Stop**: all rows show *Inactive*. Close and reopen the program: the rows
    are still there, the interface is preselected, and the server is **not** started. Start it:
    the same device gets the same address.
11. **Wrong interface.** Pick an interface on a network that has a DHCP server (e.g. the office or
    home router LAN). **Start** is refused with the "another DHCP server" message and the server's
    address. `netstat -ano -p udp` shows no `:67` owned by `DhcpServer.exe`.
12. **Cable pulled.** While running, pull the cable. Within 2 seconds the server stops and says why;
    the device receives no answer afterwards.
13. **Unavailable adapters.** Disconnected, address-less and link-local (169.254.x.x) adapters appear
    greyed in the selector with a reason tooltip and cannot be chosen. Two adapters in the same
    subnet are both marked as overlapping.
14. **Isolation.** With a second adapter connected to another host, that host receives no answer
    while the server runs on the first adapter.
15. **Exit.** Close the window while running: `netstat -ano -p udp` shows no listener from
    `DhcpServer.exe` and the device cannot renew.
16. **Read-only folder.** Run from a read-only folder: the server works, the notice that the lease
    file cannot be saved is shown, nothing else breaks.
17. **Software rendering.** Run inside a virtual machine or a Remote Desktop session: the window
    renders and the lease list updates.
18. **Firewall hint.** Start the server with the firewall blocking it and no traffic: after about a
    minute the notice about allowing the program in Windows Firewall appears.
19. **Router option.** Confirm one real IoT device still works when the server advertises the PC as
    router while Internet sharing is off (research D12).

## PC address settings (feature 002)

**Use a spare USB Ethernet adapter or a virtual machine. Never run these steps on the adapter that carries
your normal network.** Write down its settings first (`ipconfig /all`) and compare after each step.

20. **Automatic → fixed.** Spare adapter on "obtain automatically", cable to a test device. Select it: the
    "Set a fixed address" box is on and locked, a suggestion such as `192.168.137.1 / 255.255.255.0` is filled
    in. Press **Start**: the confirmation window lists *Now* and *After*; nothing has changed yet
    (`ipconfig /all`). Confirm: the adapter has the new address, the server runs, the device gets an address.
    Press **Stop**: `ipconfig /all` shows automatic mode again.
21. **Static → static with gateway and DNS.** Give the adapter a fixed address, gateway, two DNS servers and,
    if you like, a second address. Run a session with other settings, then stop: the original address(es),
    gateway and both DNS servers are back.
22. **Cancel.** Press **Cancel** in the confirmation window: nothing changed, the server did not start.
23. **Invalid input.** Try a malformed address, `255.0.255.0`, a /31 mask, a gateway in another subnet and an
    address in another connected adapter's subnet: inline messages, **Start** disabled.
24. **Crash.** Start a session, then end `DhcpServer.exe` in Task Manager. The adapter keeps the session
    address. Start the program again: it restores the old settings first and says so.
25. **Power loss.** As 24, but hard-reset the virtual machine.
26. **Changed by someone else.** Start a session, change the adapter's address in the Windows dialog, press
    **Stop**: the program leaves your change and says so.
27. **Adapter missing.** Start a session with the USB adapter, unplug it, press **Stop**: the program says it
    could not restore. Plug it in and restart the program: the settings come back (or use **Retry restoring**).
28. **Failure.** Disable the adapter while the confirmation window is open and confirm: the message names the
    failing step, the adapter is back to its old settings and the server is not running.
29. **Read-only folder.** Run from a read-only folder and choose a fixed address: it is refused with the
    message that the old settings cannot be saved; nothing changes.
30. **Two instances.** While a session with a fixed address runs, start a second instance and try the same on
    the same adapter: it refuses (another instance is changing the settings).
31. **netsh forms (Windows 7 SP1 and 11).** Confirm that setting the address by interface index works, that
    "no gateway" and "no DNS" are applied (`ipconfig /all`), that all old static addresses are replaced, and note
    how long the new address takes to become usable.
32. **Languages.** The address group, the confirmation window and the recovery banner in all six languages
    without clipping.
