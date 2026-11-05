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
