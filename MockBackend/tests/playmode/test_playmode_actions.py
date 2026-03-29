"""
test_playmode_actions.py — Integration tests for Unity action tools and events.

Requires Unity in Play Mode connected to the mock server.
Uses synchronous ServerProxy (conftest.py) — no async/await needed.

Run: uv run pytest tests/playmode/ -v
"""

import time


def test_move_to_returns_running(unity_server):
    request_id = unity_server.send_request("move_to", {"target_id": "table_lamp"})
    result = unity_server.wait_for_response(request_id, timeout=5.0)

    assert result.get("ok") is True, f"Expected ok=true for move_to, got: {result}"

    data = result.get("data", {})
    assert data.get("status") == "running", f"Expected status='running', got: {data}"
    assert data.get("action_id"), f"Expected non-null action_id, got: {data}"


def test_move_to_unknown_target_fails(unity_server):
    request_id = unity_server.send_request("move_to", {"target_id": "nonexistent_xyz"})
    result = unity_server.wait_for_response(request_id, timeout=5.0)

    assert result.get("ok") is False, f"Expected ok=false, got: {result}"


def test_interact_with_entity(unity_server):
    request_id = unity_server.send_request("interact_with", {"target_id": "table_lamp"})
    result = unity_server.wait_for_response(request_id, timeout=5.0)

    assert result.get("ok") is True, f"Expected ok=true for interact_with, got: {result}"


def test_action_completion_event(unity_server):
    """After move_to, Unity should emit action_completed or action_failed."""
    # Don't clear() here — the completion event may arrive before we start waiting.
    request_id = unity_server.send_request("move_to", {"target_id": "table_lamp"})
    ack = unity_server.wait_for_response(request_id, timeout=5.0)
    assert ack.get("ok") is True, f"move_to ACK failed: {ack}"

    action_id = ack.get("data", {}).get("action_id")
    assert action_id, "ACK must carry action_id"

    # Wait for terminal event (action may complete/fail very quickly)
    event = None
    for event_name in ("action_completed", "action_failed", "action_cancelled"):
        try:
            event = unity_server.wait_for_event(event_name, timeout=15.0)
            break
        except Exception:
            continue

    assert event is not None, "No action completion event received"

    data = event.get("data", {})
    assert "action_id" in data, f"Event missing action_id: {data}"
    assert "tool" in data, f"Event missing tool: {data}"
    assert "status" in data, f"Event missing status: {data}"


def test_equip_item(unity_server):
    # equip_item requires "item_id" (not "target_id") per ToolRegistry
    request_id = unity_server.send_request("equip_item", {"item_id": "wrench"})
    result = unity_server.wait_for_response(request_id, timeout=5.0)

    assert result.get("ok") is True, f"Expected ok=true for equip_item, got: {result}"


def test_event_dispatch_to_unity(unity_server):
    """Sending an event to Unity should not crash or disconnect it."""
    unity_server.send_event("character_state_changed", {
        "character_id": "npc_01",
        "state": "idle",
    })

    time.sleep(0.5)
    assert unity_server.is_connected, "Unity disconnected after receiving event"


def test_consecutive_actions_last_write_wins(unity_server):
    """Two rapid move_to requests: both should be acknowledged.
    If the first action is still Running when the second arrives,
    the second response includes cancelled_action_id. If the first
    already completed (e.g., NavMesh failure), no cancellation occurs.
    Either outcome is valid — this test verifies both requests succeed."""
    req_id_1 = unity_server.send_request("move_to", {"target_id": "table_lamp"})
    req_id_2 = unity_server.send_request("move_to", {"target_id": "table_lamp"})

    res_1 = unity_server.wait_for_response(req_id_1, timeout=5.0)
    res_2 = unity_server.wait_for_response(req_id_2, timeout=5.0)

    # Both requests should be acknowledged
    assert res_1.get("ok") is True, f"First move_to failed: {res_1}"
    assert res_2.get("ok") is True, f"Second move_to failed: {res_2}"

    first_action_id = res_1.get("data", {}).get("action_id")
    second_action_id = res_2.get("data", {}).get("action_id")
    assert first_action_id, f"First response missing action_id: {res_1}"
    assert second_action_id, f"Second response missing action_id: {res_2}"
    assert first_action_id != second_action_id, "Each action must have a unique ID"
