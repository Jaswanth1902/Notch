"""
generate_notch_hero.py - Deterministic Hero Video and GIF Showcase Generator
Captures the Notch 2.0 Dynamic Island HUD in action using Playwright:
- Mochi squircle procedural Canvas 2D companion with 3D gaze tracking
- Seamless Kowalski spring geometry expansions
- Two-Phase Approval Gate with amber wash and zero-focus keyboard shortcut
- Instant authorization via Shift+Enter with confetti & emerald celebration
- Exports both high-definition WebM hero video and palette-optimized GIF
"""

import os
import shutil
import time
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

def generate_showcase():
    docs_dir = Path(__file__).resolve().parent
    repo_dir = docs_dir.parent
    assets_dir = repo_dir / "assets"
    assets_dir.mkdir(exist_ok=True)
    
    video_temp_dir = repo_dir / "temp_video_rec"
    video_temp_dir.mkdir(exist_ok=True)
    frames_temp_dir = repo_dir / "temp_frames"
    frames_temp_dir.mkdir(exist_ok=True)

    showcase_html = repo_dir / "ui" / "showcase.html"
    if not showcase_html.exists():
        raise FileNotFoundError(f"Showcase HTML not found at {showcase_html}")

    print("===================================================================")
    print("  NOTCH 2.0 - HERO SHOWCASE VIDEO & GIF GENERATOR")
    print("===================================================================")
    print("[1/4] Launching Playwright Chromium with real-time video recorder...")

    frame_paths = []
    
    with sync_playwright() as p:
        browser = p.chromium.launch(
            headless=True,
            args=["--allow-file-access-from-files", "--disable-gpu-vsync"]
        )
        context = browser.new_context(
            viewport={"width": 1000, "height": 520},
            record_video_dir=str(video_temp_dir),
            record_video_size={"width": 1000, "height": 520}
        )
        page = context.new_page()
        page.goto(showcase_html.as_uri())
        page.wait_for_timeout(1000)

        def snap(step_tag, count=1, delay_ms=60):
            for i in range(count):
                idx = len(frame_paths)
                f_path = frames_temp_dir / f"frame_{idx:05d}.png"
                page.screenshot(path=str(f_path))
                frame_paths.append(f_path)
                if delay_ms > 0:
                    page.wait_for_timeout(delay_ms)

        print("[2/4] Executing 5-Stage Dynamic Island Lifecycle Simulation...")

        # ── STAGE 1: Idle Standby & Cursor Gaze Tracking ─────────────────────
        print("  - Stage 1: Idle Standby & Mochi Gaze Tracking...")
        page.evaluate("window.setScenarioState('idle')")
        snap("idle_init", count=6, delay_ms=80)

        # Smooth cursor trajectory across screen to showcase eye tracking
        gaze_points = [
            (260, 380), (320, 320), (390, 260), (460, 210),
            (520, 180), (600, 220), (700, 290), (760, 350)
        ]
        for gx, gy in gaze_points:
            page.evaluate(f"window.setCursor({gx}, {gy}, true)")
            snap("gaze_track", count=2, delay_ms=50)

        # ── STAGE 2: Background Agent Task Initiated ─────────────────────────
        print("  - Stage 2: Background Agent Task Running (Claude Code)...")
        page.evaluate("window.setScenarioState('working')")
        snap("working_start", count=10, delay_ms=70)

        # ── STAGE 3: Two-Phase Approval Gate Triggered ───────────────────────
        print("  - Stage 3: Two-Phase Approval Gate (Amber Wash + Sizing Springs)...")
        page.evaluate("""
            window.setScenarioState('approval', {
                tool: 'Execute Shell Command',
                command: 'git push origin main --tags',
                actor: 'Claude Code'
            })
        """)
        # Allow spring animation to smoothly expand from 280px to 620px
        snap("approval_expand", count=18, delay_ms=60)

        # Move cursor to hover near the Allow button
        page.evaluate("window.setCursor(440, 140, true)")
        snap("approval_hover", count=10, delay_ms=70)

        # ── STAGE 4: One-Keystroke Instant Approval & Celebration ────────────
        print("  - Stage 4: Keystroke Shift+Enter -> Instant Emerald Celebration...")
        page.evaluate("window.setScenarioState('approve_pressed')")
        snap("approved_burst", count=20, delay_ms=60)

        # ── STAGE 5: Seamless Spring Retraction ──────────────────────────────
        print("  - Stage 5: Spring Retraction to Compact Standby...")
        page.evaluate("window.setCursor(500, 400, false)")
        page.evaluate("window.setScenarioState('collapse_idle')")
        snap("collapse_spring", count=14, delay_ms=70)

        page.wait_for_timeout(600)
        context.close()
        browser.close()

    # ── Finalize WebM Hero Video ─────────────────────────────────────────────
    print("[3/4] Finalizing WebM Hero Video...")
    rec_videos = list(video_temp_dir.glob("*.webm"))
    hero_video_dest = assets_dir / "notch_hero.webm"
    if rec_videos:
        shutil.copy2(rec_videos[0], hero_video_dest)
        video_kb = round(hero_video_dest.stat().st_size / 1024, 1)
        print(f"  [SUCCESS] Hero video written to {hero_video_dest.name} ({video_kb} KB)")
    else:
        print("  [WARN] No WebM recorded by Playwright.")

    # ── Compile Palette-Optimized Quickstart GIF ──────────────────────────────
    print(f"[4/4] Compiling {len(frame_paths)} frames into palette-optimized GIF...")
    output_gif = assets_dir / "notch_quickstart.gif"

    # Load, downscale slightly to 800x416 for GitHub markdown crispness & bandwidth
    target_width = 800
    target_height = 416
    
    # We sample every 2 frames to keep GIF snappy and under 2.5MB
    sample_stride = 2
    sampled_paths = frame_paths[::sample_stride]
    
    pil_frames = []
    for fp in sampled_paths:
        with Image.open(fp) as img:
            resized = img.convert("RGBA").resize((target_width, target_height), Image.Resampling.LANCZOS)
            # Create dark matte background to prevent alpha fringing
            bg = Image.new("RGBA", resized.size, (8, 8, 11, 255))
            blended = Image.alpha_composite(bg, resized)
            # Convert to adaptive 256 color palette
            quantized = blended.convert("RGB").quantize(colors=256, method=Image.Quantize.MEDIANCUT)
            pil_frames.append(quantized)

    if pil_frames:
        pil_frames[0].save(
            output_gif,
            save_all=True,
            append_images=pil_frames[1:],
            optimize=True,
            duration=85, # ~12 fps smooth playback
            loop=0
        )
        gif_kb = round(output_gif.stat().st_size / 1024, 1)
        print(f"  [SUCCESS] Hero animated GIF written to {output_gif.name} ({gif_kb} KB)")

    # ── Cleanup Temps ────────────────────────────────────────────────────────
    shutil.rmtree(video_temp_dir, ignore_errors=True)
    shutil.rmtree(frames_temp_dir, ignore_errors=True)
    print("===================================================================")
    print("  SHOWCASE ASSETS READY FOR GITHUB SYNC!")
    print("===================================================================")

if __name__ == "__main__":
    generate_showcase()
