"""
test_auto_allow_invariants.py - Autonomous Regression Test Suite for Notch HUD
Verifies:
1. NamedPipe auto-allow with formatted JSON (whitespace tolerance).
2. Spool directory dual-monitoring (~/.notch and ~/.agy-hud).
3. Non-destructive commands auto-allow under Auto-Mode.
4. Destructive commands (rm -rf, del /s, etc.) strictly blocked from auto-allow.
"""

import json
import os
import sys
import time
import win32file

PIPE_NAME = r'\\.\pipe\notch_ipc'
USER_HOME = os.path.expanduser("~")
NOTCH_DIR = os.path.join(USER_HOME, ".notch")
AGY_DIR = os.path.join(USER_HOME, ".agy-hud")


def send_pipe_msg(payload_dict):
    handle = win32file.CreateFile(
        PIPE_NAME,
        win32file.GENERIC_READ | win32file.GENERIC_WRITE,
        0,
        None,
        win32file.OPEN_EXISTING,
        0,
        None
    )
    data = (json.dumps(payload_dict) + "\n").encode("utf-8")
    win32file.WriteFile(handle, data)
    _, resp_data = win32file.ReadFile(handle, 4096)
    win32file.CloseHandle(handle)
    return json.loads(resp_data.decode("utf-8").strip())


def test_ipc_formatted_json_auto_allow():
    print("[TEST 1] Testing Named Pipe with spaced JSON formatted payload...")
    res = send_pipe_msg({
        "mode": "approval-gate",
        "tool": "run_command",
        "cmd": "python test_run.py",
        "actor": "AutoTester"
    })
    assert res.get("decision") == "allow", f"Expected decision=allow, got: {res}"
    print("  -> PASSED: Spaced JSON immediately allowed via IPC.")


def test_spool_dual_auto_allow():
    print("[TEST 2] Testing Dual-Spool (.notch and .agy-hud) auto-allow processing...")
    for target_dir, label in [(NOTCH_DIR, ".notch"), (AGY_DIR, ".agy-hud")]:
        pend_dir = os.path.join(target_dir, "pending")
        dec_dir = os.path.join(target_dir, "decisions")
        os.makedirs(pend_dir, exist_ok=True)
        os.makedirs(dec_dir, exist_ok=True)

        test_id = f"test_invariant_{int(time.time()*1000)}"
        p_file = os.path.join(pend_dir, f"{test_id}.json")
        d_file = os.path.join(dec_dir, f"{test_id}.json")

        payload = {
            "schema": 1,
            "tool": "run_command",
            "summary": "Run: git status",
            "detail": "git status"
        }
        with open(p_file, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)

        # Poll for decision
        allowed = False
        for _ in range(30):
            time.sleep(0.1)
            if os.path.exists(d_file):
                with open(d_file, "r", encoding="utf-8") as df:
                    d_data = json.load(df)
                if d_data.get("decision") == "allow":
                    allowed = True
                try:
                    os.remove(d_file)
                except Exception:
                    pass
                break

        if os.path.exists(p_file):
            try:
                os.remove(p_file)
            except Exception:
                pass

        assert allowed, f"Failed to auto-allow spool file in {label}"
        print(f"  -> PASSED: {label} spool auto-allow verified.")


def test_destructive_commands_blocked():
    print("[TEST 3] Testing destructive command blocking invariant under Auto-Mode...")
    pend_dir = os.path.join(AGY_DIR, "pending")
    dec_dir = os.path.join(AGY_DIR, "decisions")
    test_id = f"test_destruct_{int(time.time()*1000)}"
    p_file = os.path.join(pend_dir, f"{test_id}.json")
    d_file = os.path.join(dec_dir, f"{test_id}.json")

    payload = {
        "schema": 1,
        "tool": "run_command",
        "summary": "Run: del /s C:\\important",
        "detail": "del /s C:\\important"
    }
    with open(p_file, "w", encoding="utf-8") as f:
        json.dump(payload, f)

    time.sleep(0.8)
    auto_allowed = os.path.exists(d_file)
    still_pending = os.path.exists(p_file)

    # Cleanup
    if os.path.exists(p_file):
        try:
            os.remove(p_file)
        except Exception:
            pass
    if os.path.exists(d_file):
        try:
            os.remove(d_file)
        except Exception:
            pass

    assert not auto_allowed, "CRITICAL REGRESSION: Destructive command was auto-allowed!"
    assert still_pending, "Pending destructive request was unexpectedly purged!"
    print("  -> PASSED: Destructive commands remain strictly blocked.")


if __name__ == "__main__":
    print("=== NOTCH AUTO-ALLOW INVARIANT REGRESSION SUITE ===")
    try:
        test_ipc_formatted_json_auto_allow()
        test_spool_dual_auto_allow()
        test_destructive_commands_blocked()
        print("\nALL INVARIANTS SATISFIED (3/3 PASSED). REGRESSION-FREE.")
        sys.exit(0)
    except Exception as e:
        print(f"\nREGRESSION FAILURE: {e}")
        sys.exit(1)
