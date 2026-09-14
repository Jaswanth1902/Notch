"""
generate_notch_gif.py - Deterministic Dynamic Island HUD GIF Generator
Uses Playwright + Pillow to generate a 60fps-smooth animation of the Notch HUD
morphing through its state lifecycle (Working -> Attention -> Review -> Idle).
"""

import sys
import os
import time
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

NOTCH_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background: #08080B;
    display: flex;
    justify-content: center;
    align-items: flex-start;
    width: 1000px;
    height: 480px;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    overflow: hidden;
    position: relative;
  }
  .screen-bezel {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 3px;
    background: linear-gradient(90deg, transparent, rgba(197, 160, 89, 0.4), transparent);
  }
  .camera {
    position: absolute;
    top: 6px;
    left: 50%;
    transform: translateX(-50%);
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: #151515;
    border: 1px solid #252525;
    z-index: 100;
  }
  .notch-island {
    position: absolute;
    top: 0;
    left: 50%;
    transform: translateX(-50%);
    background: #101014;
    border: 1px solid rgba(197, 160, 89, 0.5);
    border-top: none;
    border-radius: 0 0 24px 24px;
    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.8), 0 0 20px rgba(197, 160, 89, 0.15);
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 24px;
    transition: all 400ms cubic-bezier(0.23, 1, 0.32, 1);
    color: #FDFBF7;
    overflow: hidden;
  }
  /* State Styles */
  .state-idle {
    width: 220px;
    height: 36px;
  }
  .state-working {
    width: 440px;
    height: 46px;
  }
  .state-attention {
    width: 620px;
    height: 96px;
    border-color: #F59E0B;
    box-shadow: 0 14px 40px rgba(0,0,0,0.9), 0 0 25px rgba(245, 158, 11, 0.25);
  }
  .state-review {
    width: 460px;
    height: 48px;
    border-color: #A855F7;
  }

  .led {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    margin-right: 12px;
    flex-shrink: 0;
  }
  .led-blue { background: #38BDF8; box-shadow: 0 0 12px #38BDF8; }
  .led-orange { background: #F59E0B; box-shadow: 0 0 14px #F59E0B; }
  .led-purple { background: #A855F7; box-shadow: 0 0 12px #A855F7; }
  .led-green { background: #22C55E; box-shadow: 0 0 10px #22C55E; }

  .content-row {
    display: flex;
    align-items: center;
    width: 100%;
  }
  .label-group {
    display: flex;
    flex-direction: column;
    gap: 2px;
    flex: 1;
  }
  .title-text {
    font-size: 13px;
    font-weight: 600;
    color: #FDFBF7;
    letter-spacing: 0.2px;
  }
  .sub-text {
    font-size: 11px;
    color: #A1A1AA;
    font-family: 'JetBrains Mono', Consolas, monospace;
  }
  .badge-tag {
    font-size: 10px;
    padding: 3px 8px;
    border-radius: 6px;
    background: rgba(255,255,255,0.08);
    font-family: monospace;
    color: #E5C77A;
  }
  .btn-approve {
    background: #F59E0B;
    color: #000;
    font-weight: 700;
    font-size: 11px;
    padding: 6px 14px;
    border-radius: 8px;
    border: none;
    margin-left: 12px;
  }
</style>
</head>
<body>
<div class="screen-bezel"></div>
<div class="camera"></div>
<div class="notch-island state-idle" id="island">
  <div class="content-row">
    <div class="led led-green" id="led"></div>
    <div class="label-group">
      <span class="title-text" id="title">NOTCH 2.0</span>
      <span class="sub-text" id="sub">Idle • 0.0% CPU • 22MB RAM</span>
    </div>
    <span class="badge-tag" id="tag">STANDBY</span>
  </div>
</div>

<script>
  const island = document.getElementById('island');
  const led = document.getElementById('led');
  const title = document.getElementById('title');
  const sub = document.getElementById('sub');
  const tag = document.getElementById('tag');

  window.setState = function(stateName) {
    if (stateName === 'idle') {
      island.className = 'notch-island state-idle';
      led.className = 'led led-green';
      title.textContent = 'NOTCH 2.0';
      sub.textContent = 'Idle • 0.0% CPU • 22MB RAM';
      tag.textContent = 'STANDBY';
    } else if (stateName === 'working') {
      island.className = 'notch-island state-working';
      led.className = 'led led-blue';
      title.textContent = 'AI Agent Running Task...';
      sub.textContent = 'Scanning AST Symbols & Refactoring';
      tag.textContent = 'BUSY';
    } else if (stateName === 'attention') {
      island.className = 'notch-island state-attention';
      led.className = 'led led-orange';
      title.textContent = 'Confirmation Required: System Change';
      sub.textContent = 'Deploy: Apply Changes to Cluster [Shift+Enter]';
      tag.innerHTML = '<button class="btn-approve">Approve</button>';
    } else if (stateName === 'review') {
      island.className = 'notch-island state-review';
      led.className = 'led led-purple';
      title.textContent = 'Tasks Completed Successfully';
      sub.textContent = 'Ready for developer verification';
      tag.textContent = 'REVIEW';
    }
  };
</script>
</body>
</html>
"""

def generate_notch_gif():
    docs_dir = Path(__file__).resolve().parent
    repo_dir = docs_dir.parent
    assets_dir = repo_dir / "assets"
    assets_dir.mkdir(exist_ok=True)
    temp_dir = docs_dir / "temp_notch_frames"
    temp_dir.mkdir(exist_ok=True)

    html_file = docs_dir / "notch_canvas.html"
    html_file.write_text(NOTCH_HTML, encoding="utf-8")

    print("[1/3] Launching Playwright to capture Notch Dynamic Island transitions...")
    frames = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page(viewport={"width": 1000, "height": 480})
        page.goto(html_file.as_uri())
        page.wait_for_timeout(300)

        # Lifecycle Sequence: (state, frame_repeats)
        sequence = [
            ("idle", 6),
            ("working", 10),
            ("attention", 14),
            ("review", 10),
            ("idle", 8)
        ]

        frame_idx = 0
        for state_name, repeat_count in sequence:
            page.evaluate(f"window.setState('{state_name}')")
            page.wait_for_timeout(450) # allow CSS transition to finish
            
            frame_path = temp_dir / f"notch_frame_{frame_idx:04d}.png"
            page.screenshot(path=str(frame_path))
            img = Image.open(frame_path)
            
            for _ in range(repeat_count):
                frames.append(img.copy())
            frame_idx += 1

        browser.close()

    print(f"[2/3] Captured {len(frames)} frames. Compiling palette-optimized Notch GIF...")
    output_gif = assets_dir / "notch_quickstart.gif"

    frames[0].save(
        output_gif,
        save_all=True,
        append_images=frames[1:],
        optimize=True,
        duration=190,
        loop=0
    )

    gif_size_kb = round(os.path.getsize(output_gif) / 1024, 1)
    print(f"[3/3] [SUCCESS] Generated {output_gif.name} ({gif_size_kb} KB)")

    # Cleanup
    for f in temp_dir.glob("*.png"):
        f.unlink()
    temp_dir.rmdir()
    if html_file.exists():
        html_file.unlink()

if __name__ == "__main__":
    generate_notch_gif()
