/**
 * island.js - Dynamic Island HUD Controller & State Machine for Notch
 * Ported and synthesized from louis-cfm/coucou (IslandRootView & IslandStateMachine)
 * Combines Kowalski spring sizing, Mochi character placement, and non-blocking approval gates.
 */

import { Spring, Tracked, clamp } from "./mochi.js";
import { BotEngine, BOT_COLORS } from "./mochi.js";
import { Sound } from "./sound.js";

// Sizing Constants (matching Coucou & Notch 2.0 specs)
const NOTCH_W = 180;
const NOTCH_H = 34;
const COMPACT_W = 280;
const COMPACT_H = 44;
const EXPANDED_W = 620;
const EXPANDED_H = 150;
const EXPANDED_APPROVAL_H = 175;

const ROUNDED_CORNER = 20;
const EXPANDED_CORNER = 26;
const HIT_MARGIN = 14;

export class IslandController {
  constructor() {
    this.mode = "compact"; // hidden | compact | expanded
    this.view = "overview"; // overview | approval | finished | error | confused | upload
    this.isPinned = false;

    // Elements
    this.islandEl = document.getElementById("island");
    this.wakeStrip = document.getElementById("wake-strip");
    this.botCanvas = document.getElementById("bot-canvas");
    this.botGlow = document.getElementById("bot-glow");
    this.viewsEl = document.getElementById("views");
    this.countdown = document.getElementById("countdown");

    // Geometry Springs
    this.width = new Tracked(COMPACT_W);
    this.height = new Tracked(COMPACT_H);
    this.radius = new Tracked(ROUNDED_CORNER);
    this.botCx = new Spring(36);
    this.botCy = new Spring(COMPACT_H / 2);
    this.botSize = new Spring(22);

    // Mochi Engine
    this.engine = new BotEngine();
    this.engine.onSound = (cue) => Sound.play(cue);
    this.engine.onDizzy = () => this.handleDizzy();

    // Interaction State
    this.running = false;
    this.lastFrame = performance.now();
    this.mouse = { x: window.innerWidth / 2, y: 0 };
    this.autoCloseInterval = 15; // seconds
    this.autoCloseAt = null;

    // Active Pending Approval
    this.pendingApproval = null;
    this.activeSession = {
      project: "Notch",
      tool: "Ready",
      state: "idle",
      step: 0,
    };

    this.islandEl.classList.add("mode-compact");
    this.wireInput();
    this.initViews();
    this.ensureRunning();
    this.greetOnBoot();
  }

  greetOnBoot() {
    setTimeout(() => {
      this.engine.greet();
    }, 400);
  }

  wireInput() {
    // Top wake strip hover
    if (this.wakeStrip) {
      this.wakeStrip.addEventListener("mouseenter", () => {
        Sound.resume();
        if (this.mode === "hidden") {
          this.setMode("compact");
          Sound.play("peek");
        }
      });
    }

    // Island clicks & slap detection
    this.islandEl.addEventListener("mousedown", (e) => {
      Sound.resume();
      if (this.mode === "hidden") {
        this.setMode("compact");
        return;
      }
      if (this.mode === "compact") {
        this.setMode("expanded");
        return;
      }

      // Check if bot was hit
      const rect = this.islandEl.getBoundingClientRect();
      const botScreenX = rect.left + this.botCx.value;
      const botScreenY = rect.top + this.botCy.value;
      const dist = Math.hypot(e.clientX - botScreenX, e.clientY - botScreenY);

      if (dist <= this.botSize.value * 1.2) {
        this.engine.slap();
      }
    });

    // Cursor tracking
    window.addEventListener("mousemove", (e) => {
      this.mouse = { x: e.clientX, y: e.clientY };
      const rect = this.islandEl.getBoundingClientRect();

      // Project gaze relative to Mochi
      const botScreenX = rect.left + this.botCx.value;
      const botScreenY = rect.top + this.botCy.value;
      this.engine.lookX = Math.tanh((e.clientX - botScreenX) / 240);
      this.engine.lookY = -Math.tanh((e.clientY - botScreenY) / 180);

      this.ensureRunning();
    });

    // Global keyboard shortcuts (Shift+Enter to Allow, Esc to Deny / Close)
    window.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        if (this.view === "approval" && this.pendingApproval) {
          this.decide("deny");
        } else if (this.mode === "expanded") {
          this.setMode("compact");
        }
      } else if (e.key === "Enter" && e.shiftKey) {
        if (this.view === "approval" && this.pendingApproval) {
          e.preventDefault();
          this.decide("allow");
        }
      } else if (this.view === "approval" && (e.key === "y" || e.key === "Y")) {
        this.decide("allow");
      } else if (this.view === "approval" && (e.key === "n" || e.key === "N")) {
        this.decide("deny");
      }
    });
  }

  initViews() {
    // Action buttons inside approval card
    const allowBtn = document.getElementById("btn-allow");
    const denyBtn = document.getElementById("btn-deny");
    if (allowBtn) allowBtn.onclick = () => this.decide("allow");
    if (denyBtn) denyBtn.onclick = () => this.decide("deny");

    // Close button
    const closeBtn = document.getElementById("btn-close-overview");
    if (closeBtn) closeBtn.onclick = () => this.setMode("compact");
  }

  setMode(mode) {
    if (this.mode === mode) return;
    const prev = this.mode;
    this.mode = mode;

    this.islandEl.classList.remove("mode-hidden", "mode-compact", "mode-expanded");
    this.islandEl.classList.add(`mode-${mode}`);

    if (mode === "expanded") {
      Sound.play("open");
      this.scheduleAutoClose();
    } else if (prev === "expanded") {
      Sound.play("close");
      this.autoCloseAt = null;
    }

    this.animateGeometry();
  }

  setView(view) {
    this.view = view;
    document.querySelectorAll(".view").forEach((v) => v.classList.remove("on"));
    const target = document.getElementById(`view-${view}`);
    if (target) target.classList.add("on");

    this.islandEl.classList.toggle("approval-active", view === "approval");
    this.islandEl.classList.toggle("finished-active", view === "finished");

    this.animateGeometry();
  }

  animateGeometry() {
    let w = COMPACT_W;
    let h = COMPACT_H;
    let r = ROUNDED_CORNER;

    if (this.mode === "hidden") {
      w = NOTCH_W;
      h = 0;
      r = ROUNDED_CORNER;
    } else if (this.mode === "compact") {
      w = COMPACT_W;
      h = COMPACT_H;
      r = ROUNDED_CORNER;
    } else if (this.mode === "expanded") {
      w = EXPANDED_W;
      h = this.view === "approval" ? EXPANDED_APPROVAL_H : EXPANDED_H;
      r = EXPANDED_CORNER;
    }

    this.width.springTo(w, 280);
    this.height.springTo(h, 280);
    this.radius.springTo(r, 280);

    // Bot placement targets
    if (this.mode === "compact") {
      this.botCx.target = 36;
      this.botCy.target = COMPACT_H / 2;
      this.botSize.target = 22;
    } else if (this.mode === "expanded") {
      this.botCx.target = 48;
      this.botCy.target = h / 2;
      this.botSize.target = 34;
    }

    this.ensureRunning();
  }

  // ── Approval Gate API ────────────────────────────────────────────────────────

  triggerApproval(req) {
    this.pendingApproval = req;
    this.isPinned = true;

    // Update approval DOM
    const titleEl = document.getElementById("approval-title");
    const codeEl = document.getElementById("approval-code");
    const actorEl = document.getElementById("approval-actor");

    if (titleEl) titleEl.innerText = req.tool || "Permission Required";
    if (codeEl) codeEl.innerText = req.command || req.summary || req.detail || "...";
    if (actorEl) actorEl.innerText = req.actor || "AI Agent";

    this.engine.setState("approval");
    this.setMode("expanded");
    this.setView("approval");
  }

  decide(decision) {
    if (!this.pendingApproval) return;
    const req = this.pendingApproval;
    this.pendingApproval = null;
    this.isPinned = false;

    Sound.play(decision === "allow" ? "approve" : "blip");

    // Post to local server or named pipe bridge
    fetch("/api/decision", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        requestId: req.requestId || req.gate_id,
        decision: decision,
      }),
    }).catch(() => {});

    this.engine.setState(decision === "allow" ? "working" : "idle");
    this.setView("overview");
    setTimeout(() => {
      if (this.mode === "expanded" && !this.isPinned) {
        this.setMode("compact");
      }
    }, 1200);
  }

  handleDizzy() {
    this.engine.setState("dizzy");
    this.setView("confused");
    setTimeout(() => {
      this.engine.setState("idle");
      this.setView("overview");
      this.engine.triggerEmote("happy");
    }, 3300);
  }

  scheduleAutoClose() {
    if (this.isPinned) {
      this.autoCloseAt = null;
      return;
    }
    this.autoCloseAt = performance.now() + this.autoCloseInterval * 1000;
  }

  // ── Frame Loop ──────────────────────────────────────────────────────────────

  ensureRunning() {
    if (this.running) return;
    this.running = true;
    this.lastFrame = performance.now();
    requestAnimationFrame(this.frame);
  }

  frame = (nowMs) => {
    const dt = Math.min(0.05, (nowMs - this.lastFrame) / 1000);
    this.lastFrame = nowMs;

    this.width.step(dt, nowMs);
    this.height.step(dt, nowMs);
    this.radius.step(dt, nowMs);
    this.botCx.step(dt);
    this.botCy.step(dt);
    this.botSize.step(dt);

    this.applyGeometry();
    this.drawBot(dt);

    // Auto-close check
    if (this.autoCloseAt && nowMs >= this.autoCloseAt) {
      this.autoCloseAt = null;
      if (this.mode === "expanded" && !this.isPinned) {
        this.setMode("compact");
      }
    }

    // Auto-close countdown indicator
    if (this.countdown) {
      if (this.autoCloseAt && this.mode === "expanded" && !this.isPinned) {
        const remaining = Math.max(0, (this.autoCloseAt - nowMs) / (this.autoCloseInterval * 1000));
        this.countdown.style.width = `${remaining * 160}px`;
      } else {
        this.countdown.style.width = "0px";
      }
    }

    const busy =
      this.width.animating ||
      this.height.animating ||
      !this.botCx.settled ||
      !this.botCy.settled ||
      !this.botSize.settled ||
      this.engine.busy;

    if (busy || this.mode !== "hidden") {
      requestAnimationFrame(this.frame);
    } else {
      this.running = false;
    }
  };

  applyGeometry() {
    const w = this.width.value;
    const h = this.height.value;
    const r = this.radius.value;

    this.islandEl.style.width = `${Math.round(w)}px`;
    this.islandEl.style.height = `${Math.round(h)}px`;
    this.islandEl.style.borderRadius = `0 0 ${Math.round(r)}px ${Math.round(r)}px`;
  }

  drawBot(dt) {
    const size = this.botSize.value;
    const w = Math.max(1, Math.round(size));
    const h = w + 40;
    const dpr = Math.min(2, window.devicePixelRatio || 1);

    if (this.botCanvas.width !== Math.round(w * dpr)) {
      this.botCanvas.width = Math.round(w * dpr);
      this.botCanvas.height = Math.round(h * dpr);
      this.botCanvas.style.width = `${w}px`;
      this.botCanvas.style.height = `${h}px`;
    }

    this.botCanvas.style.left = `${Math.round(this.botCx.value - w / 2)}px`;
    this.botCanvas.style.top = `${Math.round(this.botCy.value - h / 2 - 4)}px`;

    const ctx = this.botCanvas.getContext("2d");
    if (!ctx) return;

    this.engine.update(dt);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    this.engine.draw(ctx, w, h);

    // Aura glow
    if (this.botGlow) {
      const col = BOT_COLORS[this.engine.state] || BOT_COLORS.idle;
      const rgbStr = `rgba(${Math.round(col[0] * 255)}, ${Math.round(col[1] * 255)}, ${Math.round(col[2] * 255)}, 0.4)`;
      this.botGlow.style.width = `${w * 2}px`;
      this.botGlow.style.height = `${w * 2}px`;
      this.botGlow.style.left = `${Math.round(this.botCx.value - w)}px`;
      this.botGlow.style.top = `${Math.round(this.botCy.value - w)}px`;
      this.botGlow.style.background = `radial-gradient(circle, ${rgbStr} 0%, transparent 70%)`;
    }
  }
}
