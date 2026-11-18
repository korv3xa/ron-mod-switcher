# RoN Mod Switcher

**RoN Mod Switcher** is a lightweight, standalone Windows (.NET 10 WPF + WebView2) mod manager and instant Vanilla switcher for **Ready or Not** (Steam & Epic Games), built by **Vosien Studio**.

- **Nexus Mods Page:** https://www.nexusmods.com/readyornot/mods/9336
- **Community & Support (Discord):** https://discord.gg/29bndFuds3

---

## Features

- **0.1s Instant Switch (Zero Duplication):** Same-volume directory/file moves for `~mods`, `Paks\mod.io`, and `C:\Users\Public\mod.io\8254` without copying gigabytes of data.
- **Steam & Epic Games Auto-Detection:** Detects both Steam and Epic installations automatically, plus manual folder selection.
- **Built-In & Custom Loadout Profiles:** Co-Op (Modded Lobby), Solo (Full Modded), Vanilla (Online), plus custom presets.
- **Co-Op Loadout Share & Compare:** Generate `RONMODS1:` share codes or `.pak` lists, compare missing/extra mods with friends, and sync your Co-Op profile in one click.
- **UE5 IoStore Bundle Grouping & Health Checks:** Automatically groups `.pak / .ucas / .utoc / .sig` companions and flags 0 KB files, missing `.utoc` / `.ucas` files, and empty mod folders.
- **Bulk Actions & Safe Management:** Filter/search mods, bulk Enable All / Disable All, drag-and-drop install (`.pak`, `.ucas`, `.utoc`, `.sig`, `.zip`, folders), and Recycle Bin delete.
- **Detailed `mod.io` Inspection:** Reads `state.json` subscriptions, `Paks\mod.io`, and `cache\mods` sizes.
- **Safe `GameUserSettings.ini` Checksum Control + 1-Click Backup Restore:** Automatically backs up `GameUserSettings.ini.ronswitch.bak` before editing and allows 1-click restore.
- **Quick Launch API Selector:** Switch between Platform Launcher (`URI`), `DX11`, and `DX12` with 6-second launch cooldown and running-game lock protection.
- **Bilingual UI:** Full English (`EN`) and Turkish (`TR`) interface.

---

## Project Structure

- `src/RoNModSwitcher.Core/` — Platform detection, mod engine, INI settings service, profiles, loadout share/compare, process service.
- `src/RoNModSwitcher.App/` — WPF + WebView2 desktop shell, JSON RPC bridge (`AppBridge`), and embedded HTML/CSS/JS UI (`wwwroot/`).
- `tests/RoNModSwitcher.Tests/` — xUnit unit and bridge end-to-end test suite.
- `docs/` — User documentation (`README_EN.txt`, `README_TR.txt`, Nexus Mods description).

---

## Build & Test Instructions

### Requirements
- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Microsoft Edge WebView2 Runtime (pre-installed on modern Windows 10/11 systems)

### Run Tests
```powershell
dotnet test "tests/RoNModSwitcher.Tests/RoNModSwitcher.Tests.csproj" -c Release
```

### Build Portable Release
```powershell
dotnet publish "src/RoNModSwitcher.App/RoNModSwitcher.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o "dist/portable"
```

---

## License

MIT License — Copyright (c) 2026 Vosien Studio
