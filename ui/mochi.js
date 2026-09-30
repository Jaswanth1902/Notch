/**
 * mochi.js - Pure Canvas 2D Ambient Character & Companion Engine for Notch
 * Ported directly from louis-cfm/coucou (NotchBuddy Swift & Windows TypeScript engine)
 * 
 * Features:
 * - Mathematical superellipse squircle body with radial specular highlights
 * - 3D spherical projection of eye gaze tracking cursor position
 * - 13 procedural eye shapes (pill, wide, dot, line, flat, happy, closed, spiral, heart, star, tired, wink, cup)
 * - Living procedural physics (blinks, breathing, bounce, roll, spark, sweat, z-particles, blush)
 * - Interactive physics: squash/slap, triple-click dizzy state, hover love emote, mailbox swallow morph
 * - Mini-bot companion generator for multi-agent fleets and integration pills
 * - Zero external dependencies (no Rive, Lottie, or Three.js)
 */

// ── Easing & Animation Primitives ─────────────────────────────────────────────

export const Ease = {
  lin: (t) => t,
  in: (t) => t * t,
  out: (t) => t * (2 - t),
  inOut: (t) => (t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t),
  back: (t) => {
    const s = 1.70158;
    return --t * t * ((s + 1) * t + s) + 1;
  },
  cubicOut: (t) => 1 - Math.pow(1 - t, 3),
};

export function lerp(a, b, t) {
  return a + (b - a) * t;
}

export function clamp(v, min, max) {
  return Math.max(min, Math.min(max, v));
}

// ── Spring Dynamics (Emil Kowalski Physics) ───────────────────────────────────

export class Spring {
  constructor(initial = 0, stiffness = 340, damping = 28) {
    this.value = initial;
    this.target = initial;
    this.velocity = 0;
    this.stiffness = stiffness;
    this.damping = damping;
    this.settled = true;
  }

  step(dt) {
    const clampedDt = Math.min(dt, 0.05);
    const force = -this.stiffness * (this.value - this.target);
    const damp = -this.damping * this.velocity;
    const accel = force + damp;

    this.velocity += accel * clampedDt;
    this.value += this.velocity * clampedDt;

    if (Math.abs(this.velocity) < 0.001 && Math.abs(this.value - this.target) < 0.001) {
      this.value = this.target;
      this.velocity = 0;
      this.settled = true;
    } else {
      this.settled = false;
    }
  }
}

export class Tracked {
  constructor(initial = 0) {
    this.value = initial;
    this.target = initial;
    this.from = initial;
    this.startMs = 0;
    this.durationMs = 280;
    this.animating = false;
    this.ease = Ease.inOut;
  }

  springTo(target, durationMs = 280) {
    if (this.target === target && this.animating) return;
    this.from = this.value;
    this.target = target;
    this.durationMs = durationMs;
    this.startMs = performance.now();
    this.ease = Ease.cubicOut;
    this.animating = true;
  }

  curveTowards(target, durationMs = 320) {
    if (this.target === target && this.animating) return;
    this.from = this.value;
    this.target = target;
    this.durationMs = durationMs;
    this.startMs = performance.now();
    this.ease = Ease.inOut;
    this.animating = true;
  }

  step(dt, nowMs) {
    if (!this.animating) return;
    const elapsed = nowMs - this.startMs;
    const progress = Math.min(1, Math.max(0, elapsed / this.durationMs));
    this.value = this.from + (this.target - this.from) * this.ease(progress);
    if (progress >= 1) {
      this.value = this.target;
      this.animating = false;
    }
  }
}

// ── Color & Geometry Helpers ──────────────────────────────────────────────────

export function hexToRGB(hex) {
  const h = hex.replace("#", "");
  const v = parseInt(h, 16);
  return [((v >> 16) & 255) / 255, ((v >> 8) & 255) / 255, (v & 255) / 255];
}

const rgba = (c, a = 1) =>
  `rgba(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)},${a})`;

const mix3 = (a, b, t) => [
  lerp(a[0], b[0], t),
  lerp(a[1], b[1], t),
  lerp(a[2], b[2], t),
];

function roundRectPath(ctx, x, y, w, h, r) {
  const radius = Math.max(0, Math.min(r, w / 2, h / 2));
  ctx.beginPath();
  ctx.moveTo(x + radius, y);
  ctx.arcTo(x + w, y, x + w, y + h, radius);
  ctx.arcTo(x + w, y + h, x, y + h, radius);
  ctx.arcTo(x, y + h, x, y, radius);
  ctx.arcTo(x, y, x + w, y, radius);
  ctx.closePath();
}

function heartPath(ctx, s) {
  ctx.beginPath();
  ctx.moveTo(0, s * 0.38);
  ctx.bezierCurveTo(-s * 1.05, -s * 0.15, -s * 0.5, -s * 0.95, 0, -s * 0.38);
  ctx.bezierCurveTo(s * 0.5, -s * 0.95, s * 1.05, -s * 0.15, 0, s * 0.38);
  ctx.closePath();
}

function starPath(ctx, ro, ri) {
  ctx.beginPath();
  for (let i = 0; i < 10; i++) {
    const r = i % 2 ? ri : ro;
    const a = -Math.PI / 2 + (i * Math.PI) / 5;
    ctx.lineTo(Math.cos(a) * r, Math.sin(a) * r);
  }
  ctx.closePath();
}

/** Ray to rounded-rect intersection for the mailbox swallow morph */
function rrPoint(ca, sa, W, H, cr) {
  const eps = 1e-6;
  const kx = ca >= 0 ? 1 : -1;
  const ky = sa >= 0 ? 1 : -1;
  const cx = kx * (W - cr);
  const cy = ky * (H - cr);

  const dot = ca * cx + sa * cy;
  const disc = dot * dot - (cx * cx + cy * cy - cr * cr);
  if (disc >= 0) {
    const t = dot + Math.sqrt(disc);
    if (t > eps) {
      const px = ca * t;
      const py = sa * t;
      if (Math.abs(px) >= W - cr - eps && Math.abs(py) >= H - cr - eps) return { x: px, y: py };
    }
  }
  if (Math.abs(sa) > eps) {
    const t = (ky * H) / sa;
    if (t > eps) {
      const px = ca * t;
      if (Math.abs(px) <= W - cr + eps) return { x: px, y: ky * H };
    }
  }
  if (Math.abs(ca) > eps) {
    const t = (kx * W) / ca;
    if (t > eps) {
      const py = sa * t;
      if (Math.abs(py) <= H - cr + eps) return { x: kx * W, y: py };
    }
  }
  return { x: kx * W, y: ky * H };
}

// ── Constants & Palettes ──────────────────────────────────────────────────────

const EYE_W = 0.25;
const EYE_H = 0.27;
const EYE_SP = 0.37;
const EYE_P = -0.12;

// Atelier obsidian base palette
const BASE_TOP = [0.93, 0.93, 0.94]; // #EDEDEF
const BASE_BOTTOM = [0.77, 0.77, 0.79]; // #C4C5CA
const INK = "rgb(26,20,18)";
const MINI_INK = "rgb(16,19,26)";
const FONT = 'system-ui, "JetBrains Mono", "Segoe UI", sans-serif';

export const BOT_COLORS = {
  idle: [0.90, 0.91, 0.93],
  working: [0.23, 0.62, 1.0], // Electric Blue
  thinking: [0.55, 0.36, 0.97], // Purple Aura
  searching: [0.39, 0.40, 0.95],
  attention: [0.96, 0.65, 0.14], // Amber Warning
  approval: [0.96, 0.65, 0.14],
  question: [0.13, 0.83, 0.93],
  error: [0.96, 0.31, 0.37], // Crimson Red
  finished: [0.20, 0.83, 0.60], // Emerald Green
  ratelimit: [0.98, 0.57, 0.24],
  sleeping: [0.58, 0.64, 0.72],
  dizzy: [0.96, 0.45, 0.71],
};

const baseState = {
  bounces: false,
  scans: false,
  breathes: false,
  zz: false,
  sweat: false,
  look: null,
  tilt: 0,
};

export const BOT_STATES = {
  idle: { ...baseState, color: BOT_COLORS.idle, tint: 0, eye: "pill", badge: null },
  working: { ...baseState, color: BOT_COLORS.working, tint: 0.72, eye: "pill", badge: { kind: "dots", color: BOT_COLORS.working } },
  thinking: { ...baseState, color: BOT_COLORS.thinking, tint: 0.72, eye: "pill", badge: { kind: "dots", color: BOT_COLORS.thinking }, look: [0.55, 0.55] },
  searching: { ...baseState, color: BOT_COLORS.searching, tint: 0.72, eye: "pill", badge: { kind: "dots", color: BOT_COLORS.searching }, scans: true },
  attention: { ...baseState, color: BOT_COLORS.attention, tint: 0.78, eye: "wide", badge: { kind: "bang", color: BOT_COLORS.attention }, bounces: true },
  approval: { ...baseState, color: BOT_COLORS.approval, tint: 0.78, eye: "wide", badge: { kind: "bang", color: BOT_COLORS.approval }, bounces: true },
  question: { ...baseState, color: BOT_COLORS.question, tint: 0.75, eye: "pill", badge: { kind: "question", color: BOT_COLORS.question }, tilt: 0.17 },
  error: { ...baseState, color: BOT_COLORS.error, tint: 0.78, eye: "flat", badge: { kind: "dot", color: BOT_COLORS.error } },
  finished: { ...baseState, color: BOT_COLORS.finished, tint: 0.35, eye: "happy", badge: { kind: "dot", color: BOT_COLORS.finished } },
  ratelimit: { ...baseState, color: BOT_COLORS.ratelimit, tint: 0.72, eye: "tired", badge: { kind: "dot", color: BOT_COLORS.ratelimit }, sweat: true },
  sleeping: { ...baseState, color: BOT_COLORS.sleeping, tint: 0.32, eye: "closed", badge: null, breathes: true, zz: true },
  dizzy: { ...baseState, color: BOT_COLORS.dizzy, tint: 0.70, eye: "spiral", badge: null },
};

const EMOTE_EYE = {
  love: "heart",
  surprised: "dot",
  proud: "star",
  wink: "wink",
  yawn: "tired",
  happy: "happy",
  annoyed: "line",
};

// ── The BotEngine (Mochi Procedural Renderer) ─────────────────────────────────

export class BotEngine {
  constructor() {
    this.isMini = false;
    this.bodyColor = null;

    // Animated transforms
    this.yaw = 0;
    this.pitch = 0;
    this.roll = 0;
    this.tilt = 0;
    this.open = 1;
    this.sx = 1;
    this.sy = 1;
    this.oy = 0;
    this.ox = 0;
    this.tint = 0;
    this.morph = 0;
    this.hands = 0;
    this.blush = 0;
    this.es = 1;
    this.badgeS = 0;

    // Target transforms
    this.tgYaw = 0;
    this.tgPitch = 0;
    this.tgTilt = 0;
    this.tgSy = 1;
    this.tgSx = 1;
    this.tgEs = 1;

    this.particleOverhang = 40;

    // Mouth slot spring for file swallowing
    this.slotH = 0;
    this.slotHTarget = 0;
    this.slotHVel = 0;
    this.isChewing = false;

    this.col = BOT_COLORS.idle;
    this.colT = BOT_COLORS.idle;

    this.state = "idle";
    this.cfg = BOT_STATES.idle;

    this.eyeOverride = null;
    this.eyeOverrideUntil = 0;
    this.permanentEye = null;
    this.permanentEmote = null;

    this.badge = null;
    this.badgeKey = "none";
    this.badgeToken = 0;

    this.tweens = new Map();
    this.locks = new Set();
    this.particles = [];

    this.lookX = 0;
    this.lookY = 0;

    this.lastTime = performance.now() / 1000;
    this.t0 = this.lastTime - Math.random() * 5;
    this.nextBlink = this.lastTime + 1.5 + Math.random() * 2;
    this.waveUntil = 0;
    this.waveStart = 0;
    this.greetToken = 0;
    this.lastAmbient = 0;
    this.slapTimes = [];

    this.onDizzy = null;
    this.onSound = null; // Callback for audio cue dispatch
  }

  playAudio(cue) {
    if (this.onSound) this.onSound(cue);
  }

  setState(next, force = false) {
    if (this.state === next && !force) return;
    const prev = this.state;
    this.state = next;
    this.cfg = BOT_STATES[next] || BOT_STATES.idle;
    this.colT = this.cfg.color;
    if (!this.locks.has("tint")) this.tint = this.cfg.tint;
    if (!this.locks.has("tilt")) this.tgTilt = this.cfg.tilt;
    this.setBadge(this.cfg.badge);

    switch (next) {
      case "finished":
        this.doRoll(950, 1);
        setTimeout(() => this.emit("spark", 5), 500);
        this.playAudio("finish");
        break;
      case "error":
        this.anim("ox", [
          [0.08, 50, Ease.out],
          [-0.08, 70, Ease.inOut],
          [0.05, 70, Ease.inOut],
          [0, 90, Ease.out],
        ]);
        this.playAudio("error");
        break;
      case "attention":
      case "approval":
        this.anim("oy", [
          [-0.2, 150, Ease.out],
          [0, 300, Ease.back],
        ]);
        this.playAudio("approval");
        break;
      case "dizzy":
        this.doRoll(1300, 2);
        this.playAudio("dizzy");
        break;
      case "question":
        this.blink();
        this.playAudio("question");
        break;
      case "ratelimit":
        this.emit("sweat", 1);
        break;
      default:
        if (prev !== "idle" || next !== "idle") this.blink();
    }
  }

  setBadge(b) {
    const key = b ? `${b.kind}-${b.color.join(",")}` : "none";
    if (key === this.badgeKey) return;
    this.badgeKey = key;
    const tok = ++this.badgeToken;
    this.anim("badgeS", [[0, 90, Ease.inOut]]);
    setTimeout(() => {
      if (tok !== this.badgeToken) return;
      this.badge = b;
      if (b) this.anim("badgeS", [[1, 280, Ease.back]]);
    }, 100);
  }

  blink() {
    if (this.locks.has("open")) return;
    this.anim("open", [
      [0.06, 70, Ease.inOut],
      [1, 130, Ease.out],
    ]);
  }

  squash() {
    this.anim("sy", [
      [0.78, 70, Ease.out],
      [1.10, 130, Ease.out],
      [1, 170, Ease.inOut],
    ]);
    this.anim("sx", [
      [1.16, 70, Ease.out],
      [0.95, 130, Ease.out],
      [1, 170, Ease.inOut],
    ]);
  }

  /** Mailbox swallow: opens slot, chews, closes */
  gulp() {
    this.slotHTarget = 0.42;
    setTimeout(() => {
      this.slotHTarget = 0;
      this.isChewing = true;
      setTimeout(() => {
        this.isChewing = false;
      }, 800);
    }, 460);
    this.anim("sy", [
      [0.78, 80, Ease.out],
      [1.18, 130, Ease.out],
      [1, 220, Ease.back],
    ]);
    this.anim("sx", [
      [1.28, 80, Ease.out],
      [0.92, 130, Ease.out],
      [1, 220, Ease.back],
    ]);
    this.blink();
    this.playAudio("approve");
  }

  slap() {
    this.interruptGreet();
    if (this.state === "dizzy") return;
    const t = performance.now() / 1000;
    this.slapTimes = this.slapTimes.filter((s) => t - s < 1.7);
    this.slapTimes.push(t);
    this.playAudio("slap");
    this.squash();

    if (this.slapTimes.length >= 3) {
      this.slapTimes = [];
      if (this.onDizzy) this.onDizzy();
    } else {
      this.eyeOverride = "line";
      this.eyeOverrideUntil = t + 0.8;
      setTimeout(() => this.playAudio("annoyed"), 60);
    }
  }

  doRoll(durationMs, turns) {
    this.roll = 0;
    this.anim("roll", [[Math.PI * 2 * turns, durationMs, Ease.inOut]], () => {
      this.roll = 0;
    });
  }

  greet() {
    const t = performance.now() / 1000;
    const tok = ++this.greetToken;
    this.waveStart = t + 0.45;
    this.waveUntil = t + 1.55;

    this.eyeOverride = "happy";
    this.eyeOverrideUntil = t + 2.0;
    this.anim("oy", [
      [-0.06, 220, Ease.out],
      [0.0, 220, Ease.back],
    ]);

    setTimeout(() => {
      if (this.greetToken !== tok) return;
      this.anim("hands", [[1, 280, Ease.out]]);
      this.anim("sy", [
        [0.95, 100, Ease.out],
        [1.0, 260, Ease.back],
      ]);
      this.anim("sx", [
        [1.04, 100, Ease.out],
        [1.0, 260, Ease.back],
      ]);
      this.playAudio("greet");
    }, 250);

    setTimeout(() => {
      if (this.greetToken === tok) this.blink();
    }, 550);
    setTimeout(() => {
      if (this.greetToken === tok) this.blink();
    }, 1500);
    setTimeout(() => {
      if (this.greetToken !== tok) return;
      this.waveUntil = 0;
      this.anim("hands", [[0, 200, Ease.inOut]]);
    }, 1550);
  }

  interruptGreet() {
    if (this.hands <= 0.01 && performance.now() / 1000 >= this.waveUntil) return;
    this.greetToken++;
    this.waveUntil = 0;
    this.waveStart = 0;
    this.anim("hands", [[0, 150, Ease.inOut]]);
  }

  triggerEmote(emote, duration = 1.8) {
    const t = performance.now() / 1000;
    this.eyeOverride = EMOTE_EYE[emote];
    this.eyeOverrideUntil = t + duration;

    switch (emote) {
      case "love":
        this.anim("blush", [
          [1, 300, Ease.out],
          [1, (duration - 0.6) * 1000, Ease.lin],
          [0, 300, Ease.inOut],
        ]);
        this.emit("heart", 4);
        this.anim("oy", [
          [-0.1, 160, Ease.out],
          [0, 300, Ease.back],
        ]);
        this.playAudio("love");
        break;
      case "surprised":
        this.anim("oy", [
          [-0.3, 140, Ease.out],
          [0, 380, Ease.back],
        ]);
        this.anim("es", [
          [1.25, 120, Ease.out],
          [1, 500, Ease.inOut],
        ]);
        break;
      case "proud":
        this.emit("star", 5);
        this.anim("tilt", [
          [-0.14, 220, Ease.out],
          [-0.14, (duration - 0.5) * 1000, Ease.lin],
          [0, 280, Ease.inOut],
        ]);
        this.anim("blush", [
          [0.7, 250, Ease.out],
          [0.7, (duration - 0.5) * 1000, Ease.lin],
          [0, 300, Ease.inOut],
        ]);
        break;
      case "wink":
        this.anim("tilt", [
          [0.12, 160, Ease.out],
          [0.12, (duration - 0.4) * 1000, Ease.lin],
          [0, 240, Ease.inOut],
        ]);
        break;
      case "yawn":
        this.anim("sy", [
          [1.12, 500, Ease.inOut],
          [1, 500, Ease.inOut],
        ]);
        this.anim("sx", [
          [0.94, 500, Ease.inOut],
          [1, 500, Ease.inOut],
        ]);
        setTimeout(() => {
          this.eyeOverride = "closed";
          this.emit("z", 2);
        }, 700);
        break;
      case "happy":
        this.anim("blush", [
          [0.6, 200, Ease.out],
          [0, 600, Ease.inOut],
        ]);
        break;
      case "annoyed":
        this.eyeOverride = "line";
        this.eyeOverrideUntil = t + 0.8;
        setTimeout(() => this.playAudio("annoyed"), 60);
        break;
    }
  }

  emit(type, count) {
    for (let i = 0; i < count; i++) {
      const isZ = type === "z";
      this.particles.push({
        type,
        x: (Math.random() - 0.5) * 0.9 + (isZ ? 0.55 : 0),
        y: -0.7 - Math.random() * 0.2,
        vx: (Math.random() - 0.5) * 0.35 + (isZ ? 0.18 : 0),
        vy: -(0.45 + Math.random() * 0.35),
        age: -i * 0.14,
        life: 1.3 + Math.random() * 0.5,
        rot: Math.random() * Math.PI * 2,
        size: 0.15 + Math.random() * 0.08,
      });
    }
  }

  animateMorph(target, durationMs) {
    const dur = durationMs ?? (target > 0.5 ? 550 : 650);
    this.anim("morph", [[target, dur, Ease.inOut]]);
  }

  resetMorph() {
    this.tweens.delete("morph");
    this.locks.delete("morph");
    this.morph = 0;
  }

  get busy() {
    return (
      this.tweens.size > 0 ||
      this.particles.length > 0 ||
      this.cfg.bounces ||
      this.cfg.scans ||
      this.cfg.breathes ||
      this.cfg.zz ||
      this.cfg.sweat ||
      this.isMini ||
      Math.abs(this.tgYaw - this.yaw) > 0.002 ||
      Math.abs(this.tgPitch - this.pitch) > 0.002 ||
      Math.abs(this.tgTilt - this.tilt) > 0.002 ||
      Math.abs(this.tgSy - this.sy) > 0.002 ||
      Math.abs(this.tgSx - this.sx) > 0.002 ||
      Math.abs(this.tgEs - this.es) > 0.002 ||
      this.slotH > 0.001 ||
      Math.abs(this.slotHVel) > 0.001 ||
      Math.abs(this.col[0] - this.colT[0]) > 0.003 ||
      Math.abs(this.col[1] - this.colT[1]) > 0.003 ||
      Math.abs(this.col[2] - this.colT[2]) > 0.003
    );
  }

  anim(prop, keys, onComplete) {
    this.tweens.set(prop, {
      prop,
      keys,
      index: 0,
      from: this[prop],
      startMs: performance.now(),
      onComplete,
    });
    this.locks.add(prop);
  }

  update(dt) {
    const n = performance.now() / 1000;
    const nowMs = performance.now();

    for (const tw of [...this.tweens.values()]) {
      const k = tw.keys[tw.index];
      const p = Math.min(1, Math.max(0, (nowMs - tw.startMs) / k[1]));
      this[tw.prop] = tw.from + (k[0] - tw.from) * k[2](p);
      if (p >= 1) {
        tw.from = k[0];
        tw.index += 1;
        tw.startMs = nowMs;
        if (tw.index >= tw.keys.length) {
          this.tweens.delete(tw.prop);
          this.locks.delete(tw.prop);
          if (tw.onComplete) tw.onComplete();
        }
      }
    }

    const t = n - this.t0;
    let ty = this.lookX * 0.62;
    let tp = this.lookY * 0.50;

    if (this.cfg.look) {
      ty = ty * 0.35 + this.cfg.look[0] * 0.55;
      tp = tp * 0.30 + this.cfg.look[1] * 0.50;
    }
    if (this.cfg.scans) {
      ty = Math.sin(t * 2.6) * 0.6;
      tp = -0.06;
    }
    if (this.state === "sleeping") {
      ty = 0;
      tp = -0.14;
    }
    if (this.state === "dizzy") {
      ty = Math.sin(t * 9) * 0.25;
    }

    this.tgYaw = ty;
    this.tgPitch = tp;
    this.tgTilt = this.cfg.tilt;

    if (n > this.waveStart && n < this.waveUntil) {
      const wt = n - this.waveStart;
      this.tgTilt = -0.06 + Math.sin(2 * Math.PI * 1.2 * wt) * 0.07;
    }

    const bounce = this.cfg.bounces ? -Math.abs(Math.sin(t * 5.2)) * 0.07 : 0;
    const kGen = 1 - Math.pow(0.0008, dt);
    if (!this.locks.has("oy")) this.oy += (bounce - this.oy) * kGen;

    if (this.cfg.breathes) {
      const amp = this.isMini ? 0.07 : 0.035;
      this.tgSy = 1 + Math.sin(t * 1.8) * amp;
      this.tgSx = 1 - Math.sin(t * 1.8) * amp * 0.57;
    } else if (this.isMini) {
      this.tgSy = 1 + Math.sin(t * 2.2) * 0.04;
      this.tgSx = 1 - Math.sin(t * 2.2) * 0.02;
    } else {
      this.tgSy = 1;
      this.tgSx = 1;
    }

    const kLook = 1 - Math.pow(0.0025, dt);
    if (!this.locks.has("yaw")) this.yaw += (this.tgYaw - this.yaw) * kLook;
    if (!this.locks.has("pitch")) this.pitch += (this.tgPitch - this.pitch) * kLook;
    if (!this.locks.has("tilt")) this.tilt += (this.tgTilt - this.tilt) * kGen;
    if (!this.locks.has("sy")) this.sy += (this.tgSy - this.sy) * kGen;
    if (!this.locks.has("sx")) this.sx += (this.tgSx - this.sx) * kGen;
    if (!this.locks.has("es")) this.es += (this.tgEs - this.es) * kGen;

    this.col = mix3(this.col, this.colT, 1 - Math.pow(0.002, dt));

    if (n > this.nextBlink) {
      if (this.state !== "sleeping" && this.state !== "dizzy") {
        this.blink();
        if (Math.random() < 0.22) setTimeout(() => this.blink(), 230);
      }
      this.nextBlink = n + 2.2 + Math.random() * 3.2;
    }

    if (this.eyeOverride && n > this.eyeOverrideUntil) {
      this.eyeOverride = this.permanentEye;
      if (this.permanentEye) this.eyeOverrideUntil = Number.POSITIVE_INFINITY;
    }

    if (n - this.lastAmbient > 1.3) {
      this.lastAmbient = n;
      if (this.cfg.zz) this.emit("z", 1);
      if (!this.isMini && this.cfg.sweat && Math.random() < 0.5) this.emit("sweat", 1);
    }

    for (const p of this.particles) p.age += dt;
    this.particles = this.particles.filter((p) => p.age < p.life);

    // Mouth slot spring physics
    const omega = (2 * Math.PI) / 0.25;
    const zeta = 0.6;
    const acc = omega * omega * (this.slotHTarget - this.slotH) - 2 * zeta * omega * this.slotHVel;
    this.slotHVel += acc * dt;
    this.slotH = Math.max(0, this.slotH + this.slotHVel * dt);

    this.lastTime = n;
  }

  // ── Canvas 2D Draw Pipeline ─────────────────────────────────────────────────

  draw(ctx, W, H) {
    const R = W * 0.3;
    const rx = R * 1.14;
    const ry = R * 0.88;
    const cx = W / 2 + this.ox * R;
    const cy = H / 2 + this.particleOverhang / 2 + this.oy * R + R * 0.06;

    this.drawHandsBehind(ctx, R, rx, ry, cx, cy);

    ctx.save();
    ctx.translate(cx, cy);
    if (this.tilt !== 0) ctx.rotate(this.tilt);
    ctx.scale(this.sx, this.sy);

    const body = this.bodyPath(rx, ry, R);
    this.drawBody(ctx, body, R, rx, ry);

    const blushVal = Math.max(this.blush, this.tint * 0.5) * (1 - this.morph);
    if (blushVal > 0.01) {
      ctx.save();
      ctx.clip(body);
      const yOffset = Math.sin(this.yaw) * rx * 0.8;
      ctx.fillStyle = `rgba(255,120,150,${0.5 * blushVal})`;
      for (const sd of [-1, 1]) {
        ctx.beginPath();
        ctx.ellipse(sd * rx * 0.55 + yOffset, ry * 0.2, R * 0.17, R * 0.1, 0, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();
    }

    this.drawEyes(ctx, body, R, rx, ry);
    if (this.morph > 0.05) this.drawMouth(ctx, body, R);

    ctx.restore();

    if (this.badge && this.badgeS > 0.01 && this.morph < 0.25) {
      this.drawBadge(ctx, this.badge, R, cx, cy);
    }
    this.drawParticles(ctx, R, cx, cy);
  }

  bodyPath(rx, ry, R) {
    const n = 72;
    const expN = 2.0 / 2.7; // Squircle formula
    const tw = R * 1.0;
    const th = R * 0.94;
    const tr = R * 0.42;
    const p = new Path2D();
    const m = this.morph;

    for (let i = 0; i <= n; i++) {
      const a = (i / n) * Math.PI * 2;
      const ca = Math.cos(a);
      const sa = Math.sin(a);
      const px0 = rx * (ca >= 0 ? Math.pow(ca, expN) : -Math.pow(-ca, expN));
      const py0 = ry * (sa >= 0 ? Math.pow(sa, expN) : -Math.pow(-sa, expN));
      let px = px0;
      let py = py0;

      if (m >= 0.005) {
        const rr = rrPoint(ca, sa, tw, th, tr);
        px = lerp(px0, rr.x, m);
        py = lerp(py0, rr.y, m);
      }
      if (i === 0) p.moveTo(px, py);
      else p.lineTo(px, py);
    }
    p.closePath();
    return p;
  }

  drawBody(ctx, body, R, rx, ry) {
    if (this.bodyColor) {
      ctx.fillStyle = rgba(this.bodyColor, 1);
      ctx.fill(body);
      return;
    }
    const g = ctx.createLinearGradient(rx * 0.7, -ry * 0.85, -rx * 0.8, ry * 0.9);
    g.addColorStop(0, rgba(BASE_TOP));
    g.addColorStop(1, rgba(BASE_BOTTOM));
    ctx.fillStyle = g;
    ctx.fill(body);

    const effectiveTint = this.tint * (1 - this.morph);
    if (effectiveTint > 0.01) {
      const tg = ctx.createLinearGradient(0, ry, 0, -ry);
      tg.addColorStop(0, rgba(this.col, 0.72 * effectiveTint));
      tg.addColorStop(1, rgba(this.col, 0));
      ctx.fillStyle = tg;
      ctx.fill(body);
    }

    const sh = ctx.createRadialGradient(0, 0, R * 0.15, 0, 0, R * 1.25);
    sh.addColorStop(0, "rgba(0,0,0,0)");
    sh.addColorStop(0.6, "rgba(0,0,0,0)");
    sh.addColorStop(1, "rgba(0,0,0,0.2)");
    ctx.fillStyle = sh;
    ctx.fill(body);

    const hl = ctx.createRadialGradient(rx * 0.34, -ry * 0.46, 0, rx * 0.34, -ry * 0.46, R * 0.42);
    hl.addColorStop(0, "rgba(255,255,255,0.55)");
    hl.addColorStop(1, "rgba(255,255,255,0)");
    ctx.fillStyle = hl;
    ctx.fill(body);
  }

  drawEyes(ctx, body, R, rx, ry) {
    let shape = this.eyeOverride || this.cfg.eye;
    if (this.morph > 0.5) {
      if (this.isChewing) shape = "happy";
      else if (this.slotHTarget > 0.05 || this.slotH > 0.1) shape = "cup";
    }

    ctx.save();
    ctx.clip(body);
    const ink = this.isMini ? MINI_INK : INK;
    ctx.fillStyle = ink;
    ctx.strokeStyle = ink;

    for (const sd of [-1, 1]) {
      const eyeYaw = sd * EYE_SP + this.yaw;
      let eyePitch = EYE_P + this.pitch + this.roll;
      eyePitch = (((eyePitch + Math.PI) % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2) - Math.PI;
      const cp = Math.cos(eyePitch);
      if (Math.cos(eyeYaw) * cp <= 0.04) continue;

      const ex = Math.sin(eyeYaw) * cp * rx;
      const ey = -Math.sin(eyePitch) * ry + (this.morph > 0 ? ry * 0.14 * this.morph : 0);
      const fx = lerp(Math.max(0.18, Math.cos(eyeYaw)), 1, this.morph * 0.7);
      const fy = lerp(Math.max(0.18, cp), 1, this.morph * 0.7);
      const eyeMult = this.isMini ? 1.9 : 1.0;
      const ew = R * EYE_W * this.es * eyeMult;
      const eh = R * EYE_H * this.es * eyeMult;

      ctx.save();
      ctx.translate(ex, ey);
      ctx.scale(fx, fy);
      this.drawEyeShape(ctx, shape, ew, eh, sd, ink);
      ctx.restore();
    }
    ctx.restore();
  }

  drawEyeShape(ctx, shape, w, h, sd, ink) {
    const t = performance.now() / 1000;
    switch (shape) {
      case "wide":
        this.drawEyeShape(ctx, "pill", w * 1.16, h * 1.12, sd, ink);
        break;
      case "pill": {
        const hh = Math.max(h * this.open, w * 0.3);
        roundRectPath(ctx, -w / 2, -hh / 2, w, hh, Math.min(w / 2, hh / 2));
        ctx.fill();
        break;
      }
      case "dot":
        ctx.beginPath();
        ctx.arc(0, 0, w * 0.45, 0, Math.PI * 2);
        ctx.fill();
        break;
      case "line":
        ctx.rotate(-sd * 0.2);
        roundRectPath(ctx, -w * 0.78, -w * 0.21, w * 1.56, w * 0.42, w * 0.21);
        ctx.fill();
        break;
      case "flat":
        roundRectPath(ctx, -w * 0.72, -w * 0.2, w * 1.44, w * 0.4, w * 0.2);
        ctx.fill();
        break;
      case "happy":
        ctx.lineWidth = w * 0.5;
        ctx.lineCap = "round";
        ctx.beginPath();
        ctx.arc(0, h * 0.18, w * 0.82, Math.PI * 1.12, Math.PI * 1.88);
        ctx.stroke();
        break;
      case "closed":
        ctx.lineWidth = w * 0.36;
        ctx.lineCap = "round";
        ctx.beginPath();
        ctx.arc(0, -h * 0.08, w * 0.78, Math.PI * 0.15, Math.PI * 0.85);
        ctx.stroke();
        break;
      case "spiral": {
        ctx.lineWidth = w * 0.22;
        ctx.lineCap = "round";
        ctx.beginPath();
        for (let a = 0; a < 4.4 * Math.PI; a += 0.2) {
          const r = w * 0.06 + a * w * 0.058;
          const aa = a + t * 9 * sd;
          const px = Math.cos(aa) * r;
          const py = Math.sin(aa) * r;
          if (a === 0) ctx.moveTo(px, py);
          else ctx.lineTo(px, py);
        }
        ctx.stroke();
        break;
      }
      case "heart":
        ctx.fillStyle = "#FF4D6D";
        heartPath(ctx, w * 1.2);
        ctx.fill();
        ctx.fillStyle = ink;
        break;
      case "star":
        ctx.fillStyle = "#F7B32B";
        ctx.rotate(t * 1.5 * sd);
        starPath(ctx, w * 1.05, w * 0.46);
        ctx.fill();
        ctx.fillStyle = ink;
        break;
      case "tired":
        roundRectPath(ctx, -w / 2, -h * 0.02, w, h * 0.38, w / 2);
        ctx.fill();
        roundRectPath(ctx, -w * 0.62, -h * 0.1, w * 1.24, w * 0.22, w * 0.11);
        ctx.fill();
        break;
      case "wink":
        if (sd < 0) {
          const hh = Math.max(h * this.open, w * 0.3);
          roundRectPath(ctx, -w / 2, -hh / 2, w, hh, Math.min(w / 2, hh / 2));
          ctx.fill();
        } else {
          ctx.lineWidth = w * 0.5;
          ctx.lineCap = "round";
          ctx.beginPath();
          ctx.arc(0, h * 0.18, w * 0.82, Math.PI * 1.12, Math.PI * 1.88);
          ctx.stroke();
        }
        break;
      case "cup": {
        const hh = Math.max(h * this.open, w * 0.3);
        const cr = Math.min(w / 2, hh / 2);
        ctx.beginPath();
        ctx.moveTo(-w / 2, -hh / 2);
        ctx.lineTo(w / 2, -hh / 2);
        ctx.lineTo(w / 2, hh / 2 - cr);
        ctx.quadraticCurveTo(w / 2, hh / 2, w / 2 - cr, hh / 2);
        ctx.lineTo(-w / 2 + cr, hh / 2);
        ctx.quadraticCurveTo(-w / 2, hh / 2, -w / 2, hh / 2 - cr);
        ctx.closePath();
        ctx.fill();
        break;
      }
    }
  }

  drawMouth(ctx, body, R) {
    const m = this.morph;
    const hW = R * 1.8 * m;
    const hH = this.slotH * R * m;
    const hX = -hW / 2;
    const boxTop = -R * (0.88 + 0.06 * m);
    const hY = boxTop + R * 0.08 * m;

    ctx.save();
    ctx.clip(body);

    ctx.strokeStyle = `rgba(255,255,255,${0.55 * m})`;
    ctx.lineWidth = 1;
    ctx.lineCap = "round";
    ctx.beginPath();
    ctx.moveTo(-R * 0.9 * m, boxTop + 1);
    ctx.lineTo(R * 0.9 * m, boxTop + 1);
    ctx.stroke();

    if (hH > 0.8) {
      const hR = Math.min(hW / 2, hH / 2);
      const g = ctx.createLinearGradient(0, hY, 0, hY + hH);
      g.addColorStop(0, "rgb(7,8,10)");
      g.addColorStop(1, "rgb(16,19,26)");
      roundRectPath(ctx, hX, hY, hW, hH, hR);
      ctx.fillStyle = g;
      ctx.fill();
    }
    ctx.restore();
  }

  drawHandsBehind(ctx, R, rx, ry, cx, cy) {
    if (this.hands <= 0.01 || this.isMini) return;
    const n = performance.now() / 1000;
    const bodyH = 2 * ry;
    const hew = 0.3 * ry * this.hands;
    const heh = 0.26 * ry * this.hands;
    const hwB = rx * this.sx;
    const hhB = ry * this.sy;
    const isWaving = n >= this.waveStart && this.waveStart > 0 && n < this.waveUntil;

    for (const sd of [-1, 1]) {
      let localX, localY, handRot = 0;
      if (sd > 0 && isWaving) {
        const wt = n - this.waveStart;
        const rise = Math.min(1, wt / 0.18);
        const riseEased = 1 - Math.pow(1 - rise, 3);
        const restX = hwB * 1.08;
        const restY = hhB * 0.70;
        const oscX = Math.cos(13 * wt) * 0.06 * bodyH;
        const oscY = -Math.sin(13 * wt) * 0.14 * bodyH;
        const waveX = hwB * 1.1 + oscX;
        const waveY = -hhB * 0.15 + oscY;
        localX = restX + (waveX - restX) * riseEased;
        localY = restY + (waveY - restY) * riseEased;
        handRot = (-0.5 + Math.sin(13 * wt) * 0.35) * riseEased;
      } else {
        localX = sd * hwB * 1.08;
        localY = hhB * 0.70;
      }

      const cosT = Math.cos(this.tilt);
      const sinT = Math.sin(this.tilt);
      const worldX = cx + cosT * localX - sinT * localY;
      const worldY = cy + sinT * localX + cosT * localY;

      ctx.save();
      ctx.translate(worldX, worldY);
      if (handRot !== 0) ctx.rotate(handRot);
      const g = ctx.createLinearGradient(hew * 0.7, -heh * 0.85, -hew * 0.8, heh * 0.9);
      g.addColorStop(0, rgba(BASE_TOP));
      g.addColorStop(1, rgba(BASE_BOTTOM));
      ctx.beginPath();
      ctx.ellipse(0, 0, hew, heh, 0, 0, Math.PI * 2);
      ctx.fillStyle = g;
      ctx.fill();
      ctx.restore();
    }
  }

  drawBadge(ctx, badge, R, cx, cy) {
    const bs = this.badgeS * (this.isMini ? 1.25 : 1);
    const bx = cx - R * 0.72 * this.sx;
    const by = cy - R * 0.72 * this.sy;
    const t = performance.now() / 1000;

    ctx.save();
    ctx.translate(bx, by);
    ctx.scale(bs, bs);
    const col = rgba(badge.color);

    if (badge.kind === "dots") {
      const pw = R * 0.72;
      const ph = R * 0.36;
      roundRectPath(ctx, -pw / 2, -ph / 2, pw, ph, ph / 2);
      ctx.fillStyle = col;
      ctx.fill();
      for (let i = 0; i < 3; i++) {
        const phase = (((t * 2.4 - i * 0.22) % 1) + 1) % 1;
        const dotR = R * 0.055 * (1 + 0.4 * Math.max(0, Math.sin(phase * Math.PI * 2)));
        ctx.fillStyle = "#fff";
        ctx.beginPath();
        ctx.arc((i - 1) * R * 0.18, 0, dotR, 0, Math.PI * 2);
        ctx.fill();
      }
    } else if (badge.kind === "bang" || badge.kind === "question") {
      ctx.fillStyle = "#000";
      ctx.beginPath();
      ctx.arc(0, 0, R * 0.3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = col;
      ctx.beginPath();
      ctx.arc(0, 0, R * 0.23, 0, Math.PI * 2);
      ctx.fill();
      if (!this.isMini) {
        ctx.fillStyle = "#fff";
        ctx.font = `900 ${R * 0.32}px ${FONT}`;
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(badge.kind === "bang" ? "!" : "?", 0, R * 0.02);
      }
    }
    ctx.restore();
  }

  drawParticles(ctx, R, cx, cy) {
    for (const p of this.particles) {
      if (p.age <= 0) continue;
      const k = p.age / p.life;
      const a = k < 0.2 ? k / 0.2 : 1 - (k - 0.2) / 0.8;
      const px = cx + (p.x + p.vx * p.age) * R * 1.3;
      const py = cy + (p.y + p.vy * p.age) * R * 1.3;
      const sz = R * p.size * (1 + k * 0.4);

      ctx.save();
      ctx.translate(px, py);
      ctx.globalAlpha = Math.min(1, Math.max(0, a));
      switch (p.type) {
        case "heart":
          ctx.rotate(Math.sin(p.age * 6) * 0.3);
          ctx.fillStyle = "#FF4D6D";
          heartPath(ctx, sz);
          ctx.fill();
          break;
        case "star":
          ctx.rotate(p.rot + p.age * 2);
          ctx.fillStyle = "#F7B32B";
          starPath(ctx, sz, sz * 0.45);
          ctx.fill();
          break;
        case "spark":
          ctx.rotate(p.rot);
          ctx.fillStyle = "#fff";
          starPath(ctx, sz * 0.8, sz * 0.18);
          ctx.fill();
          break;
        case "sweat":
          ctx.fillStyle = "#7CC7FF";
          ctx.beginPath();
          ctx.moveTo(0, -sz);
          ctx.quadraticCurveTo(sz * 0.8, sz * 0.2, 0, sz * 0.6);
          ctx.quadraticCurveTo(-sz * 0.8, sz * 0.2, 0, -sz);
          ctx.fill();
          break;
        case "z":
          ctx.fillStyle = "rgb(209,219,235)";
          ctx.font = `700 ${sz * 1.9}px ${FONT}`;
          ctx.textAlign = "center";
          ctx.textBaseline = "middle";
          ctx.fillText("z", 0, 0);
          break;
      }
      ctx.restore();
    }
  }
}
