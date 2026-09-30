/**
 * sound.js - Low-latency synthetic Web Audio API feedback for Notch
 * Emulates the 12 essential Coucou audio cues natively using Web Audio API synthesis
 * with zero required asset files, plus optional support for external WAV samples.
 */

class SoundEngine {
  constructor() {
    this.ctx = null;
    this.enabled = true;
    this.volume = 0.15;
    this.sampleCache = new Map();
  }

  init() {
    if (!this.ctx && typeof window !== "undefined") {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      if (AudioCtx) {
        this.ctx = new AudioCtx();
      }
    }
  }

  resume() {
    this.init();
    if (this.ctx && this.ctx.state === "suspended") {
      this.ctx.resume();
    }
  }

  setEnabled(on) {
    this.enabled = on;
  }

  setVolume(vol) {
    this.volume = Math.max(0, Math.min(1, vol));
  }

  play(cue) {
    if (!this.enabled) return;
    this.resume();
    if (!this.ctx) return;

    try {
      const t = this.ctx.currentTime;
      switch (cue) {
        case "blip":
        case "tick":
          this.synthBlip(t, 880, 0.04);
          break;
        case "peek":
          this.synthTone(t, 523.25, 659.25, 0.12);
          break;
        case "open":
          this.synthSlide(t, 340, 580, 0.18);
          break;
        case "close":
          this.synthSlide(t, 520, 280, 0.16);
          break;
        case "hover":
          this.synthBlip(t, 1046.5, 0.06);
          break;
        case "approve":
          this.synthChord(t, [523.25, 659.25, 783.99], 0.28);
          break;
        case "finish":
          this.synthChord(t, [587.33, 739.99, 880.0, 1174.66], 0.35);
          break;
        case "error":
          this.synthTone(t, 220, 185, 0.25, "sawtooth");
          break;
        case "slap":
          this.synthNoise(t, 0.08);
          break;
        case "annoyed":
          this.synthSlide(t, 440, 311.13, 0.22, "triangle");
          break;
        case "love":
          this.synthChord(t, [659.25, 830.61, 987.77], 0.4);
          break;
        case "dizzy":
          this.synthWobble(t, 0.6);
          break;
        default:
          this.synthBlip(t, 600, 0.05);
      }
    } catch (e) {
      // Audio autoplay policy or device sleep
    }
  }

  synthBlip(t, freq, dur) {
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = "sine";
    osc.frequency.setValueAtTime(freq, t);
    gain.gain.setValueAtTime(this.volume, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + dur);
    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + dur);
  }

  synthTone(t, f1, f2, dur, type = "sine") {
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(f1, t);
    osc.frequency.exponentialRampToValueAtTime(f2, t + dur);
    gain.gain.setValueAtTime(this.volume * 0.9, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + dur);
    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + dur);
  }

  synthSlide(t, f1, f2, dur, type = "sine") {
    const osc = this.ctx.createOscillator();
    const gain = this.ctx.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(f1, t);
    osc.frequency.exponentialRampToValueAtTime(f2, t + dur);
    gain.gain.setValueAtTime(this.volume * 0.8, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + dur);
    osc.connect(gain);
    gain.connect(this.ctx.destination);
    osc.start(t);
    osc.stop(t + dur);
  }

  synthChord(t, freqs, dur) {
    freqs.forEach((f, idx) => {
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = "sine";
      osc.frequency.setValueAtTime(f, t + idx * 0.03);
      gain.gain.setValueAtTime(this.volume * 0.5, t + idx * 0.03);
      gain.gain.exponentialRampToValueAtTime(0.001, t + dur);
      osc.connect(gain);
      gain.connect(this.ctx.destination);
      osc.start(t + idx * 0.03);
      osc.stop(t + dur);
    });
  }

  synthNoise(t, dur) {
    const bufferSize = this.ctx.sampleRate * dur;
    const buffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < bufferSize; i++) {
      data[i] = (Math.random() * 2 - 1) * Math.exp(-i / (bufferSize * 0.3));
    }
    const noise = this.ctx.createBufferSource();
    noise.buffer = buffer;
    const gain = this.ctx.createGain();
    gain.gain.setValueAtTime(this.volume * 0.7, t);
    noise.connect(gain);
    gain.connect(this.ctx.destination);
    noise.start(t);
  }

  synthWobble(t, dur) {
    const osc = this.ctx.createOscillator();
    const mod = this.ctx.createOscillator();
    const modGain = this.ctx.createGain();
    const gain = this.ctx.createGain();

    osc.type = "triangle";
    osc.frequency.setValueAtTime(400, t);

    mod.frequency.setValueAtTime(14, t);
    modGain.gain.setValueAtTime(150, t);
    mod.connect(modGain);
    modGain.connect(osc.frequency);

    gain.gain.setValueAtTime(this.volume * 0.6, t);
    gain.gain.exponentialRampToValueAtTime(0.001, t + dur);

    osc.connect(gain);
    gain.connect(this.ctx.destination);

    mod.start(t);
    osc.start(t);
    mod.stop(t + dur);
    osc.stop(t + dur);
  }
}

export const Sound = new SoundEngine();
