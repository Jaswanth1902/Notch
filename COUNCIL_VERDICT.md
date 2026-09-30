# 🏛️ The Council Verdict: Coucou Architectural Exploration & Notch Integration

> **Target**: Comprehensive Analysis of `louis-cfm/coucou` & Integration Roadmap for Notch  
> **Repository Context**: `Jaswanth1902/Notch` (`scratch/repos/notch`) & Antigravity OS (`05_Services/agy-hud`)  
> **Deliberation Engine**: 5-Persona Independent Review & Chairman Synthesis  
> **Date**: 2026-09-30  

---

## 1. Executive Summary

A comprehensive architectural inspection of `louis-cfm/coucou` reveals a remarkably mature, production-grade Dynamic Island implementation spanning Swift 6 (macOS) and Tauri 2 / Rust + TypeScript (Windows). Rather than a generic notification widget, Coucou solves the fundamental friction of background AI coding agents through two breakthrough pillars: (1) a zero-dependency Canvas 2D ambient character (Mochi) providing sub-symbolic state awareness, and (2) an asynchronous, fail-closed named pipe relay (`coucou-hook.exe`) with a two-phase ACK/Decision gate that guarantees external CLI sessions (Claude Code, Cursor, Antigravity) are never wedged or blocked. The Council unanimously approves a 4-phase technical synthesis integrating Coucou's non-blocking relay protocol, Canvas 2D character engine, and Win32 OLE drag-and-drop fix into Notch 2.0 while replacing heavy Rust Tauri boilerplate with our planned lightweight C# .NET 9 AOT / Webview2 hybrid architecture.

---

## 2. Forensic Breakdown of `louis-cfm/coucou`

Coucou consists of two complete implementations sharing design tokens and sound assets:
- **macOS (`NotchBuddy/`)**: Pure Swift 6 / SwiftUI / AppKit borderless `NSPanel` sitting flush in the MacBook camera notch. Zero third-party packages.
- **Windows (`windows/`)**: Tauri 2 (Rust backend + vanilla TypeScript frontend). Borderless, transparent, always-on-top window with click-through hit-testing and a system tray presence.

### Key Architectural Gems Discovered:
1. **The Pure Canvas 2D Character Engine (`windows/src/mochi/engine.ts`)**:
   - 100% vector mathematics with zero external animation runtimes (no Rive, Lottie, or Three.js).
   - Superellipse formula ($x = r_x \cdot \cos(a)^{2/2.7}$, $y = r_y \cdot \sin(a)^{2/2.7}$) producing Apple-grade squircles.
   - 3D sphere projection of pupil gaze coordinates towards screen cursor using hyperbolic tangent distance damping ($\tanh(\Delta x / 260)$).
   - 13 distinct procedural eye shapes and continuous spring dynamics (blinks, breathing sine waves, approval bounce, sleep z-particles, finish sparks).
   - Interactive slap/squash physics (3 rapid clicks $\to$ dizzy spiral eyes and double-rotation roll).
2. **The Non-Blocking CLI Hook Relay (`windows/hook/src/main.rs`)**:
   - Hard Rule: *Never block Claude Code*. Relay connects to `\\.\pipe\coucou-<sid>` in $<300\text{ms}$. If server is absent, immediately exits 0 with empty stdout, falling back silently to the terminal prompt.
   - Security: Windows user SID appended to pipe path, verifying client and server owner SIDs match before data transfer.
   - Payload truncation: Strips redundant massive blobs (`tool_response`, `transcript_path`), caps strings at 2,000 chars to avoid memory bloat.
3. **The Two-Phase Decision IPC Protocol (`windows/src-tauri/src/pipe.rs`)**:
   - `ACK_TIMEOUT` (800ms): Validates whether the UI is active and has rendered the approval card. If hidden, paused, or wedged, immediately declines so CLI terminal takes over.
   - `DECISION_TIMEOUT` (108s): Bounded human interaction window before automatic release.
4. **The Win32 WebView2 OLE Drop Fix (`windows/src-tauri/src/island.rs`)**:
   - Solves the infamous WebView2 Windows bug where `Chrome_RenderWidgetHostHWND` rejects OS drag-and-drop events: invokes `RevokeDragDrop` via Win32 API, routing OLE drops cleanly to the parent window target.
   - Smooth mailbox swallowing animation (`slotH` spring) upon file drop.
5. **Zero-Power Window Collapse**:
   - Retracts to a $240 \times 6\text{px}$ invisible wake strip when idle; parks the cursor polling thread on a Win32 `Condvar`, achieving true 0.00% idle CPU and 0 frame paints.

---

## 3. Stage 1: Independent Persona Deliberations

### 📐 The Systems Architect
- **Critique**: Coucou’s Windows architecture uses Tauri 2 (Rust + Vite + WebView2). While substantially lighter than Electron, Tauri’s multi-process model (Rust host + WebView2 runtime) carries a working set baseline of ~45–65 MB RAM. Notch’s target in `NOTCH_2.0_ARCHITECTURE_SPEC.md` is $\le 12\text{ MB RAM}$.
- **Recommendation**: Decouple the frontend logic from Tauri. The Mochi Canvas 2D engine and island state machine are written in pure TypeScript without framework dependencies. They can run directly in a lightweight C# .NET 9 AOT host embedding `Microsoft.Web.WebView2.Core` or a native DirectComposition surface. We must adopt Coucou’s pipe protocol and state machine while maintaining Notch’s single-binary deployment footprint.

### 🛡️ The Security Warden
- **Critique**: Coucou’s named pipe security is exceptionally well engineered: (1) `pipe_name()` uses the user SID, preventing multi-user collision; (2) `pipe_server_is_same_user()` checks handle ownership tokens; (3) `FIRST_PIPE_INSTANCE` rejects rogue pipe hijackers.
- **Mandate**: Notch currently uses PowerShell and file-based spooling (`~/.notch/pending/`). Spool directories are vulnerable to race conditions and file-system watchers. We must immediately port Coucou’s Win32 named pipe security architecture to Notch (`\\.\pipe\notch-<sid>`) with explicit DACL security descriptors allowing only `TOKEN_USER`. Furthermore, Doberman HMAC verification (`05_Services/doberman_notch_adapter.py`) must be wired into this pipe before approvals are displayed.

### ⚡ The Performance & Efficiency Engineer
- **Critique**: Coucou's cursor polling thread runs at ~60 Hz (`sleep(16ms)`) querying `GetCursorPos` and evaluating click-through bounding boxes. While parked on condvar when hidden, when visible it consumes 1–2% CPU on older hardware.
- **Recommendation**: Retain the wake-strip collapse mechanism (0.00% idle CPU when resting). For the visible state, replace the active 60Hz polling loop with Windows Raw Input hooks (`WM_INPUT`) or low-level mouse hooks (`WH_MOUSE_LL`), waking the render loop only on genuine mouse motion across the top 100px zone.

### 🎨 The Craftsmanship & UI/UX Arbiter
- **Critique**: Coucou’s tactile physics and character design are extraordinary. The subtle breathing cycle, mouse gaze tracking, squish on click, and 28 handcrafted audio cues transform a sterile utility into an intuitive desktop companion.
- **Standard**: Integrate the Mochi Canvas 2D engine directly into Notch. Blend Coucou’s squircle physics with Notch’s Da Vinci blueprint linework and Atelier obsidian palette (`#0B0B0D` surface with subtle `#C5A059` gold accents). Preserve the 28 sound effects using low-latency DirectSound/wasapi audio playback.

### 🥊 The Contrarian (Devil's Advocate & Pixar Shaded Penny Test)
- **Challenge**: "Is adding an animated character an unnecessary toy that distracts from developer productivity?"
- **Verdict**: Passed the Shaded Penny Test. In long-running autonomous workflows (e.g. multi-step test suites, agent swarms, background builds), developers suffer from chronic Alt-Tab fatigue. A peripheral, ambient indicator that communicates *working*, *thinking*, *needs attention*, or *finished* via glanceable non-textual cues (color aura, posture, glance direction) dramatically reduces cognitive overhead without requiring reading text. However, the chat prompt view in Coucou is redundant with existing IDEs and should be deprioritized; Notch must remain an execution and approval HUD first.

---

## 4. Stage 2: Cross-Examination & Clash Resolutions

| Friction Point | Deliberating Personas | Resolution / Strategic Choice |
| :--- | :--- | :--- |
| **Runtime Stack: Tauri 2 vs C# AOT + WebView2** | Architect vs Contrarian | Reject full Tauri rewrite. Keep Notch on a single-binary .NET 9 AOT / WebView2 architecture, extracting Coucou’s vanilla TypeScript Canvas engine into Notch’s UI surface. |
| **Polling vs Windowless Hooks** | Performance vs Security | Adopt Coucou’s 240x6px wake strip and condvar parking. Visible hit-testing will be gated by mouse entry into the top display boundary. |
| **Character vs Raw Metrics** | Craftsmanship vs Contrarian | Dual-mode presentation: Left pill houses the Mochi status companion; right drawer expands to show Herdr multi-agent panes and Doberman security diffs. |

---

## 5. Chairman Synthesis: The 4-Phase Integration Directive

### Phase 1: IPC Pipe & Relay Engine (`notch-relay`)
- Implement standalone native relay `notch-relay.exe` ported from Coucou's `windows/hook/src/main.rs`.
- Wire `\\.\pipe\notch-<sid>` with user SID binding and `FIRST_PIPE_INSTANCE`.
- Implement two-phase ACK timeout (800ms UI ACK $\to$ 108s Decision timeout) with fail-closed non-blocking terminal fallback.

### Phase 2: Mochi Canvas 2D Engine Integration
- Extract `windows/src/mochi/engine.ts` into Notch’s UI layer (`05_Services/agy-hud/ui/` and `scratch/repos/notch/src/ui/`).
- Bind agent lifecycle states to Mochi postures:
  - `working`: Blue aura, animated breathing, working dots badge.
  - `attention`: Amber aura, bounce animation, bang badge (`!`), global `Shift+Enter` approval hook.
  - `review` / `finished`: Emerald green, celebration sparks, roll animation.
  - `error`: Crimson red, flat eyes, shake animation.

### Phase 3: Win32 Subsystem & OLE Drag/Drop Parity
- Incorporate Coucou's `RevokeDragDrop` routine for WebView2 child handles to enable native file ingestion.
- Implement $240 \times 6\text{px}$ wake-strip window collapse for true 0.00% idle CPU.
- Implement click-through dynamic bounding box switching via `WS_EX_TRANSPARENT`.

### Phase 4: Multi-Agent & Antigravity Mesh Bridging
- Connect `05_Services/herdr_notch_bridge.py` into Coucou's mini-bot pill grid to display background subagents as mini companions.
- Connect `05_Services/doberman_notch_adapter.py` into the approval card for instant HMAC-verified tool inspection.

---

## 6. Verdict
- **Status**: **APPROVED (Go)**
- **Immediate Next Step**: Stop execution and present the architectural verdict and executive brief to the user for phase prioritization.
