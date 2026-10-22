# Security policy

Security fixes target the latest published version. Older releases are not maintained separately.

## Reporting a vulnerability

Use [Security → Report a vulnerability](https://github.com/fajraii/island-monitor/security/advisories/new) for private disclosure. Include the version, reproduction steps, impact, and a suggested fix if available. Do not include secrets, private files, or personal monitoring data. Please avoid public issues for unpatched vulnerabilities. This is a volunteer project; no response-time guarantee is offered.

## Application behavior

Island Monitor reads local Windows memory statistics, physical-disk performance counters, and selected-drive free space. It stores preferences in `%LOCALAPPDATA%\IslandMonitor\settings.xml`. It does not collect telemetry, send network requests, download updates, or require administrator privileges in normal use. Disk counters can be restricted or unavailable. GitHub's release download and build workflows operate separately from the app.

Release executables are unsigned. Download from this repository's Releases page and compare the ZIP's SHA-256 hash with its accompanying checksum file. Checksums detect accidental changes; they do not replace a trusted source or code signing.
