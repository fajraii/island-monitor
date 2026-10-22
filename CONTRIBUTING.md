# Contributing

Bug reports, accessibility improvements, documentation, and focused pull requests are welcome.

## Development

Use Windows 10 or 11, Windows PowerShell 5.1, and .NET Framework 4.x. No NuGet packages or separate .NET SDK are needed.

```powershell
git clone https://github.com/fajraii/island-monitor.git
cd island-monitor
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\smoke.ps1
.\bin\IslandMonitor.exe
```

Quit the app from its tray menu before rebuilding the executable you are running. Tests create a temporary widget and tray icon, restore preview changes, and do not write your preferences. An interactive Windows desktop is recommended for manual UI checks; CI performs WPF layout smoke checks on a Windows runner.

## Project layout

| File | Purpose |
| --- | --- |
| `IslandMonitor.cs` | WPF interface, Windows counters, tray lifecycle, preferences |
| `SettingsTheme.xaml` | Settings window control styles |
| `AssemblyInfo.cs` / `VERSION` | Executable metadata and release version |
| `make-icon.ps1` | Original logo and multi-resolution icon generator |
| `build.ps1` | Compile into `bin/` |
| `tests/smoke.ps1` | Threshold, layout, preview, settings, and resource checks |
| `scripts/package.ps1` | Portable ZIP and SHA-256 checksum |

## Pull requests

1. Open an issue first for a substantial behavior or architecture change.
2. Create a focused branch and explain the problem and resulting behavior.
3. Run the build and smoke tests. Include screenshots for visual changes.
4. Check dragging, expanding/collapsing, tray show/hide/quit, settings save/cancel, and keyboard navigation when affected.
5. Do not commit generated executables, local preferences, credentials, or monitoring history.

Use clear names, preserve existing style around edits, and keep documentation aligned with behavior. Contributions are provided under the MIT license. Treat others respectfully; discuss ideas and code without personal attacks or harassment.

## Releases

Update `VERSION`, the three version attributes in `AssemblyInfo.cs`, and `CHANGELOG.md` together, then merge to `main`. Test and package locally with `scripts/package.ps1`. Push a matching `vX.Y.Z` tag to run the release workflow. It checks the tag against `VERSION`, builds and tests on Windows, and publishes a portable ZIP with SHA-256 checksums. Binaries are unsigned.
