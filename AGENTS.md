# Notch HUD Repository Constitution & Governance (Local Layer 1)

This repository contains **Notch**, an ambient, hardware-accelerated Dynamic Island and AI Agent HUD for Windows 11. Any agent, subagent, or developer working within this repository MUST follow the laws, invariants, and delivery gates codified below.

---

## 1. Architectural Stack & Physical Reality

- **Primary Runtime**: Native C# WPF application compiled via standard .NET Framework CSC (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- **Low-Level Hook**: Native Win32 keyboard hook (`notch-hook.cs` -> `bin\notch-hook.exe` and `~/.agy-hud/hooks/agy-hook.exe`) registering `SetWindowsHookEx` / `WH_KEYBOARD_LL`.
- **Fast-Path IPC**: Win32 Named Pipe `\\.\pipe\notch_ipc` (`PipeTransmissionMode.Byte`, non-blocking async pump).
- **Resilient Spool Queues**: Dual file-system queues mirroring `~/.notch` and `~/.agy-hud` (`pending/`, `decisions/`, `sessions/`, `state.json`).
- **Resource Constraints**: Strict performance budgets: `<25 MB` private working set, `0.0%` idle CPU, zero Alt-Tab pollution (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`).

---

## 2. Mandatory Core Invariants (Non-Negotiable)

### 1. The Dual-Spool & Unified Mirror Invariant
- Every file queue watcher in `NotchApp.cs` MUST poll both `~/.notch` and `~/.agy-hud`.
- Every hook, adapter, or agent writing decisions, states, or session cards MUST atomic-broadcast to both directories.
- Assuming only `~/.notch` exists is categorically forbidden; legacy and external callers frequently write to `~/.agy-hud`.

### 2. The Whitespace-Tolerant JSON Parsing Law
- NEVER use brittle substring or whitespace-sensitive matches (e.g. `":\""`, `line.Contains("\"mode\":\"approval-gate\"")`).
- Standard serializers in Python, Node, and external CLIs insert arbitrary whitespace after colons (`{"mode": "approval-gate"}`).
- All key extraction MUST use regex patterns that handle optional whitespace, unescaped inner quotes, and both quoted strings and numeric/boolean tokens.

### 3. The Non-Destructive Auto-Allow Invariant
- When Auto-Mode is active (`_isAutoMode` or `auto_mode.flag` in either spool), non-destructive actions MUST auto-allow immediately without interrupting the user or opening modal attention panels.
- Carrier Inspection: The decision engine MUST evaluate all carrier fields: `tool`, `cmd`, `summary`, `detail`, and `diff`.
- If Auto-Mode clears all pending items while the HUD is in `attention`, the HUD must automatically dismiss the modal and return to `idle` or `working`.

### 4. The Destructive Command Shield (Double-Press Confirmation)
- Even when Auto-Mode is ON, destructive commands MUST NEVER be auto-allowed.
- Patterns matching `rm -rf`, `del /s`, `format`, `drop table`, `drop database`, `truncate`, `git push --force`, or `git reset --hard` MUST trigger the amber/red attention gate.
- Destructive commands require explicit two-stroke confirmation (`CONFIRM (Press 2x)`) armed within a 1.8-second safety window.

### 5. Fullscreen / Gaming Minimal Line Auto-Collapse
- During fullscreen applications, games, or presentation mode (`MonitorFromWindow` + `rcMonitor` bounding check), Notch MUST automatically collapse to a 1.5px minimal white line.
- Manual expansion (`_isManualExpanded`) must reset when entering fullscreen. Double-clicking the minimal line expands it; exiting fullscreen restores standard pill geometry.

### 6. Semantic Earcon & Accurate State Invariant
- Earcons (harmonic chimes C6 1046Hz -> E6 1318Hz) MUST strictly play ONLY upon true task completion or approved review.
- Never signal completion or fire completion earcons when an agent simply pauses between turns for user input or tool execution; turn pauses map to `state: "idle"` ("Ready for instructions").

### 7. Session TTL & Liveness Pruning
- Session cards in `~/.notch/sessions` and `~/.agy-hud/sessions` older than 300 seconds (5 minutes) whose originating process PID has terminated MUST be automatically purged by `GetMergedSessions()`. Zombie cards must never clutter the session drawer.

### 8. Windows Process Visibility & Subshell Escaping Law
- Any background process spawned from subshells MUST use `CREATE_NO_WINDOW` (`0x08000000`) or WMI process creation (`Invoke-CimMethod -ClassName Win32_Process -MethodName Create`).
- When running PowerShell commands via CLI tools, NEVER embed bare `$` variable declarations inside double-quoted CLI command strings. Write helper scripts to `.ps1` / `.py` or use single-quoted blocks.

---

## 3. Directory Layout & Roles

```
scratch/repos/notch/
├── assets/             # Vector SVGs, banners, and documentation media (<25KB budget)
├── bin/                # Compiled native binaries (notch-app.exe, notch-hook.exe)
├── docs/               # Architecture guides and API documentation
├── hooks/              # Shell and batch entrypoints for CLI integration
├── specs/              # Technical specifications (NOTCH_2.0_ARCHITECTURE_SPEC.md)
├── src/
│   ├── NotchApp.cs     # Main WPF dynamic island UI & NamedPipe/Spool daemon
│   └── NotchCore.cs    # Standalone headless daemon core
├── tests/
│   └── test_auto_allow_invariants.py # Regression verification suite
├── build.ps1           # Official CSC build script + test verification gate
├── start.bat           # Interactive detached launcher
├── stop.bat            # Clean process terminator
├── notch-hook.cs       # Native C# global keyboard hook & CLI hook binary
└── AGENTS.md           # This document (Repository Constitution)
```

---

## 4. Build, Deployment & Verification Workflow

### Compilation
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```
This compiles `src\NotchApp.cs` with native WPF assemblies:
- `PresentationFramework.dll`
- `PresentationCore.dll`
- `WindowsBase.dll`
- `System.Xaml.dll`
- `System.dll` / `System.Core.dll`

### Regression Testing
All changes to IPC, parsing, or spool logic must pass the automated invariant test suite before merging:
```bash
python tests/test_auto_allow_invariants.py
```

### Hook Deployment
When `notch-hook.cs` is modified, copy the compiled binary to the active Antigravity CLI hook location:
```powershell
Copy-Item bin\notch-hook.exe C:\Users\jaswa\.agy-hud\hooks\agy-hook.exe -Force
```

### Process Lifecycle
- **Start**: Run `start.bat` or launch detached via `scratch/launch_notch.ps1` into Windows Session 1 (`JASWANTH\jaswa`).
- **Stop**: Run `stop.bat` to terminate all instances of `notch-app.exe`, `notch-hook.exe`, and `notch-core.exe`.

---

## 5. Quintuple Delivery Gate Checklist for Notch

Before declaring any change or feature deliverable in this repository:
- [ ] **Gate 1 (CSC Syntax & Compilation)**: Clean compile via `build.ps1` with zero warnings or errors.
- [ ] **Gate 2 (Automated Invariant Suite)**: `python tests/test_auto_allow_invariants.py` passes all tests (IPC whitespace, dual-spool, destructive blocking).
- [ ] **Gate 3 (Named Pipe Probe)**: Live Named Pipe probe to `\\.\pipe\notch_ipc` returns valid JSON response in <50ms.
- [ ] **Gate 4 (UI/UX Craft & Physical Geometry)**: Floating island is flush with top monitor bezel (`Top = 0`), renders liquid glass gradient, and correctly auto-collapses in fullscreen.
- [ ] **Gate 5 (OS Reality Verification)**: Process verified running on Session 1 (`tasklist | findstr /i notch-app`) with memory <25MB.
