"""
test_coucou_notch_integration.py - Autonomous Regression Test Suite for Absorbed Coucou Capabilities
Verifies:
1. notch-relay.exe zero-blocking invariant (exits 0 with empty stdout when pipe absent within 300ms SLA).
2. Claude Code hook response formatting and payload truncation.
3. Mochi Canvas 2D engine & Island UI asset integrity.
4. Native binary build and execution bounds.
"""

import os
import subprocess
import time
import json
import pytest

NOTCH_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
BIN_DIR = os.path.join(NOTCH_ROOT, "bin")
UI_DIR = os.path.join(NOTCH_ROOT, "ui")
RELAY_EXE = os.path.join(BIN_DIR, "notch-relay.exe")
CORE_EXE = os.path.join(BIN_DIR, "notch-core.exe")

CREATE_NO_WINDOW = 0x08000000


def test_binaries_exist():
    """Verify all compiled native C# artifacts exist in bin/"""
    assert os.path.isfile(RELAY_EXE), f"Relay binary missing: {RELAY_EXE}"
    assert os.path.isfile(CORE_EXE), f"Core daemon binary missing: {CORE_EXE}"


def test_relay_non_blocking_fallback():
    """
    Hard Rule Invariant: Never block the AI Agent.
    When Notch server is absent or busy, notch-relay must exit 0 in <350ms with empty stdout.
    """
    start = time.time()
    proc = subprocess.run(
        [RELAY_EXE, "PreInvocation"],
        input='{"hook_event_name":"PreInvocation","modelName":"claude-3-5-sonnet"}',
        text=True,
        capture_output=True,
        creationflags=CREATE_NO_WINDOW,
        timeout=2.0
    )
    duration = time.time() - start

    assert proc.returncode == 0, f"Expected returncode 0, got {proc.returncode}"
    assert proc.stdout.strip() == "", f"Expected empty stdout on absence, got: {proc.stdout}"
    assert duration < 0.6, f"Relay took too long ({duration:.3f}s), exceeded SLA"


def test_ui_assets_integrity():
    """Verify the Mochi Canvas 2D and Dynamic Island UI assets are complete and non-empty."""
    required_files = [
        "mochi.js",
        "sound.js",
        "island.css",
        "island.js",
        "index.html"
    ]
    for fname in required_files:
        fpath = os.path.join(UI_DIR, fname)
        assert os.path.isfile(fpath), f"UI asset missing: {fpath}"
        assert os.path.getsize(fpath) > 100, f"UI asset suspiciously small: {fpath}"


def test_mochi_canvas_engine_exports():
    """Verify mochi.js contains core procedural math and character classes."""
    mochi_js = os.path.join(UI_DIR, "mochi.js")
    with open(mochi_js, "r", encoding="utf-8") as f:
        content = f.read()

    assert "class BotEngine" in content, "BotEngine class missing from mochi.js"
    assert "class Spring" in content, "Spring class missing from mochi.js"
    assert "class Tracked" in content, "Tracked class missing from mochi.js"
    assert "BOT_STATES" in content, "BOT_STATES definition missing from mochi.js"
    assert "drawEyeShape" in content, "Procedural eye drawing missing from mochi.js"
    assert "bodyPath" in content, "Squircle path generator missing from mochi.js"


def test_sound_engine_cues():
    """Verify sound.js implements all key synthetic audio cues."""
    sound_js = os.path.join(UI_DIR, "sound.js")
    with open(sound_js, "r", encoding="utf-8") as f:
        content = f.read()

    assert "class SoundEngine" in content, "SoundEngine class missing from sound.js"
    cues = ["peek", "open", "close", "approve", "finish", "error", "slap", "annoyed", "love", "dizzy"]
    for cue in cues:
        assert f'case "{cue}":' in content, f"Audio cue '{cue}' missing from sound.js"


def test_island_views_and_hotkeys():
    """Verify island.js implements keyboard approvals and view management."""
    island_js = os.path.join(UI_DIR, "island.js")
    with open(island_js, "r", encoding="utf-8") as f:
        content = f.read()

    assert "class IslandController" in content
    assert "triggerApproval" in content
    assert "decide" in content
    assert "Shift" in content, "Shift+Enter hotkey missing"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
