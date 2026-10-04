<div align="center">

# 🏝️ Notch
### The Ambient Dynamic Island for Windows 11. Zero Latency. Hardware Accelerated.

[![Release](https://img.shields.io/badge/Windows-11%20Ready-0078D4?style=flat-square&logo=windows)](https://github.com/Jaswanth1902/Notch)
[![RAM](https://img.shields.io/badge/RAM-~38MB-brightgreen?style=flat-square)]()
[![Idle CPU](https://img.shields.io/badge/Idle%20CPU-<0.3%25-brightgreen?style=flat-square)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

**A physical Dynamic Island floating at the top of your Windows screen.**  
Monitors your background AI agents, build pipelines, audio playback, and active system context with fluid tactile motion and zero window-focus theft.

[🚀 One-Line Install](#quickstart) • [⚡ Why Notch?](#why-notch) • [📐 Architecture](#architecture)

</div>

---

### ⚡ Why Notch?
- **Zero Focus Theft**: Floats on `winsta0\Default` without triggering `cmd.exe` flashes or stealing window focus.
- **Hardware-Accelerated Physics**: Custom fluid spring damping curves running at a locked 60 FPS.
- **Agent Telemetry Built-In**: Watch background coding agents execute tools, tests, and builds without switching terminal tabs.
- **Extreme Memory Economy**: Native Windows execution consuming under **40MB RAM** and **<0.3% idle CPU**.

---

### 🚀 Quickstart

```powershell
# Clone and run directly
git clone https://github.com/Jaswanth1902/Notch.git
cd Notch
pip install -r requirements.txt
python notch.py
```

---

### 📐 Architecture
Notch monitors a dual-spool directory (`~/.notch/` and `~/.agy-hud/`) via asynchronous Win32 filesystem watcher events. External scripts, CLI tools, and agent daemons emit lightweight JSON telemetry payloads to the spool to instantly project visual updates on screen.

```json
{"event": "agent_action", "title": "Pytest Passing", "detail": "46 tests green (1.1s)", "status": "success"}
```
