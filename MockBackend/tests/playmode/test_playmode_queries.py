"""
test_playmode_queries.py — Integration tests for Unity query tools.

Requires Unity in Play Mode connected to the mock server.
Uses synchronous ServerProxy (conftest.py) — no async/await needed.

Run: uv run pytest tests/playmode/ -v
"""

import time

RESPONSE_TIMEOUT = 5.0


def test_get_inventory(unity_server):
    request_id = unity_server.send_request("get_inventory", {})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is True, f"Expected ok=true, got: {result}"

    data = result.get("data", {})
    assert "items" in data, f"Response data missing 'items' field: {data}"

    items = data["items"]
    assert isinstance(items, list), f"'items' should be a list, got: {type(items)}"
    assert len(items) == 3, f"Expected 3 default items, got {len(items)}: {items}"

    item_ids = {item.get("item_id", "") for item in items}
    assert "wrench" in item_ids, f"Expected wrench in items: {item_ids}"
    assert "shovel" in item_ids, f"Expected shovel in items: {item_ids}"
    assert "postcard" in item_ids, f"Expected postcard in items: {item_ids}"


def test_get_player_state(unity_server):
    request_id = unity_server.send_request("get_player_state", {})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is True, f"Expected ok=true, got: {result}"

    data = result.get("data", {})
    assert "position" in data, f"Response data missing 'position': {data}"
    assert "rotation" in data, f"Response data missing 'rotation': {data}"
    assert "scene" in data, f"Response data missing 'scene': {data}"


def test_get_world_summary(unity_server):
    request_id = unity_server.send_request("get_world_summary", {})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is True, f"Expected ok=true, got: {result}"

    data = result.get("data", {})
    assert "scene" in data, f"Missing 'scene': {data}"
    assert "player_position" in data, f"Missing 'player_position': {data}"
    assert "nearby_entity_count" in data, f"Missing 'nearby_entity_count': {data}"
    assert "has_active_action" in data, f"Missing 'has_active_action': {data}"


def test_get_nearby_entities(unity_server):
    time.sleep(1.5)  # Avoid QPS rate limit from previous tests
    request_id = unity_server.send_request("get_nearby_entities", {"radius": 50})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is True, f"Expected ok=true, got: {result}"

    data = result.get("data", {})
    assert "entities" in data, f"Response data missing 'entities': {data}"
    assert isinstance(data["entities"], list)


def test_invalid_tool(unity_server):
    request_id = unity_server.send_request("nonexistent_tool_xyzzy", {})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is False, f"Expected ok=false for unknown tool, got: {result}"


def test_missing_required_param(unity_server):
    time.sleep(1.5)  # Avoid QPS rate limit from previous tests
    request_id = unity_server.send_request("move_to", {})
    result = unity_server.wait_for_response(request_id, timeout=RESPONSE_TIMEOUT)

    assert result is not None, "No response received from Unity"
    assert result.get("ok") is False, f"Expected ok=false, got: {result}"

    result_str = str(result).upper()
    assert "INVALID_PARAMS" in result_str or "INVALID_PARAM" in result_str, \
        f"Expected INVALID_PARAMS error code: {result}"
