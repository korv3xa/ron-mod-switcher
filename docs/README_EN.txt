===============================================================================
  RoN Mod Switcher v1.0.0 — Ready or Not Mod Manager & Vanilla Switcher
  By Vosien Studio
===============================================================================

OVERVIEW
--------
RoN Mod Switcher lets you manage both Nexus Mods (~mods) and in-game mod.io
mods for Ready or Not without duplicating gigabytes of .pak files.
Same-volume moves switch between Modded and clean Vanilla Online in milliseconds.


KEY FEATURES
------------
1. Instant Vanilla <-> Modded Switching (Zero File Duplication)
   - Moves ~mods, Paks\mod.io, and C:\Users\Public\mod.io\8254 out of the game's
     load path in milliseconds and restores them cleanly on demand.

2. Steam & Epic Games Support
   - Automatically detects Steam and Epic Games installations (or lets you
     pick any custom Ready or Not folder manually).

3. Built-In Loadout Profiles + Custom Profiles
   - Co-Op (Modded Lobby): Activates your chosen co-op mod list and disables
     Server Side Checksum so friends with mismatched mods can join.
   - Solo (Full Modded): Full single-player loadout that automatically includes
     newly installed mods.
   - Vanilla (Online): Stashes all ~mods and mod.io content cleanly and restores
     Server Side Checksum=True for public matchmaking.

4. Co-Op Loadout Share & Compare
   - Export any profile as a compact RONMODS1: share code and readable mod list.
   - Paste a friend's RONMODS1: code or plain .pak list to immediately see:
     * Matching mods
     * Missing locally (need to download)
     * Extra enabled locally (disable before joining)
   - One-click sync updates your Co-Op profile to match shared mods you own.

5. Smart UE5 Mod Manager & Health Checks
   - Automatically groups .pak / .ucas / .utoc / .sig (IoStore) bundles into
     a single toggleable unit.
   - Filter & Search + Bulk Enable All / Disable All for filtered results.
   - Drag-and-drop install for .pak, .ucas, .utoc, .sig, .zip, or mod folders.
   - Automatic package health warnings: flags 0 KB empty files, missing .utoc
     or .ucas companion files, and empty mod folders.
   - Safe uninstall moves mods to the Windows Recycle Bin.

6. Detailed mod.io Inspection
   - Reads in-game mod.io subscriptions (metadata\state.json), Paks\mod.io
     packages, and cache\mods size directly alongside your ~mods list.

7. Safe GameUserSettings.ini Editing & 1-Click Backup Restore
   - Automatically saves GameUserSettings.ini.ronswitch.bak before making
     its first edit.
   - Restore your original INI anytime with one click in Settings & Studio.

8. Quick Launch API Selector & Safety Locks
   - Choose Launcher (Steam/Epic URI), DX11, or DX12 directly from Mission
     Control.
   - Single-instance lock and automatic Game Running detection prevent file
     corruption while Ready or Not is open.
   - Bilingual Turkish / English (TR / EN) interface.


USAGE
-----
1. Extract RoNModSwitcher.exe to any folder (no installation required).
2. Make sure Ready or Not is closed before switching mods.
3. Run RoNModSwitcher.exe and choose a profile (Co-Op, Solo, or Vanilla).
4. Use Mod Manager to toggle individual mods, bulk toggle filtered lists,
   or drag-and-drop .zip/.pak archives to install.
5. Note: On Windows 10/11, the Microsoft Edge WebView2 Runtime is required
   (pre-installed on almost all Windows 10/11 PCs).


SUPPORT & COMMUNITY
-------------------
Vosien Studio Discord: https://discord.gg/29bndFuds3
