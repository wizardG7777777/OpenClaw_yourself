"""
test_protocol.py — Server-side WebSocket frame protocol tests.

These tests verify that MockMCPServer correctly implements the MCP frame protocol.
They run WITHOUT Unity: both sides of the WebSocket are driven by this test file.

Frame protocol:
  Server → Unity  request : {"type": "req",   "id": "req_xxx", "method": "...", "params": {...}}
  Unity  → Server response: {"type": "res",   "id": "req_xxx", "ok": true/false, "data": {...}}
  Server → Unity  event   : {"type": "event", "event": "...", "data": {...}}
  Unity  → Server event   : {"type": "event", "event": "action_completed",
                              "data": {"action_id": "...", "tool": "...",
                                       "status": "Completed", "result": {...}}}
"""

import asyncio
import json
import pytest
import websockets

from mock_server.server import MockMCPServer

SERVER_URL = "ws://localhost:8765/ws"


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture
async def server():
    """Start MockMCPServer before each test, stop it after."""
    srv = MockMCPServer()
    await srv.start()
    yield srv
    await srv.stop()


@pytest.fixture
async def client(server):
    """Open a websockets connection to the running mock server."""
    async with websockets.connect(SERVER_URL) as ws:
        yield ws


# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------


async def _recv_json(ws, timeout: float = 2.0) -> dict:
    """Receive one WebSocket message and parse it as JSON."""
    raw = await asyncio.wait_for(ws.recv(), timeout=timeout)
    return json.loads(raw)


async def _send_json(ws, payload: dict) -> None:
    """Serialize *payload* as JSON and send it over *ws*."""
    await ws.send(json.dumps(payload))


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


async def test_server_starts_and_accepts_connection(server):
    """Server starts and a plain websockets client can connect."""
    async with websockets.connect(SERVER_URL) as ws:
        # websockets v16 uses ws.state; v13 had ws.open
        assert ws.state.name == "OPEN", "WebSocket connection should be open after connecting"


async def test_send_request_frame_format(server):
    """
    After the server calls send_request, the connected client receives a frame
    that conforms to the request protocol:
      {"type": "req", "id": <str>, "method": <str>, "params": <dict>}
    """
    async with websockets.connect(SERVER_URL) as ws:
        # Give the server a moment to register the new connection
        await asyncio.sleep(0.05)

        method = "get_player_position"
        params = {"world": "main"}
        request_id = await server.send_request(method, params)

        frame = await _recv_json(ws)

        assert frame["type"] == "req"
        assert frame["id"] == request_id
        assert frame["method"] == method
        assert frame["params"] == params


async def test_receive_response_matches_request(client, server):
    """
    When the client sends a well-formed response frame, server.wait_for_response
    returns a dict that contains the correct data.
    """
    await asyncio.sleep(0.05)

    request_id = await server.send_request("ping", {})

    # Consume the request frame so the client's receive buffer stays clear
    await _recv_json(client)

    response_data = {"pong": True, "timestamp": 12345}
    await _send_json(client, {
        "type": "res",
        "id": request_id,
        "ok": True,
        "data": response_data,
    })

    result = await server.wait_for_response(request_id, timeout=2.0)

    assert result is not None
    assert result.get("ok") is True
    assert result.get("data") == response_data


async def test_send_event_frame_format(server):
    """
    After the server calls send_event, the connected client receives a frame
    that conforms to the event protocol:
      {"type": "event", "event": <str>, "data": <dict>}
    """
    async with websockets.connect(SERVER_URL) as ws:
        await asyncio.sleep(0.05)

        event_name = "scene_loaded"
        event_data = {"scene": "mvp_game", "entities": 42}
        await server.send_event(event_name, event_data)

        frame = await _recv_json(ws)

        assert frame["type"] == "event"
        assert frame["event"] == event_name
        assert frame["data"] == event_data


async def test_receive_event_from_client(client, server):
    """
    When the client sends an action_completed event frame, server.wait_for_event
    returns the matching event dict.
    """
    await asyncio.sleep(0.05)

    event_payload = {
        "type": "event",
        "event": "action_completed",
        "data": {
            "action_id": "act_001",
            "tool": "move_player",
            "status": "Completed",
            "result": {"position": {"x": 1.0, "y": 0.0, "z": 2.0}},
        },
    }
    await _send_json(client, event_payload)

    received = await server.wait_for_event("action_completed", timeout=2.0)

    assert received is not None
    data = received.get("data", {})
    assert data["action_id"] == "act_001"
    assert data["tool"] == "move_player"
    assert data["status"] == "Completed"


async def test_multiple_requests_responses_matched_correctly(client, server):
    """
    Send three requests, respond in reverse order; each wait_for_response must
    return the data that corresponds to its own request id.
    """
    await asyncio.sleep(0.05)

    id_a = await server.send_request("method_a", {"n": 1})
    id_b = await server.send_request("method_b", {"n": 2})
    id_c = await server.send_request("method_c", {"n": 3})

    # Drain the three request frames from the client's buffer
    for _ in range(3):
        await _recv_json(client)

    # Respond in reverse order: C, A, B
    await _send_json(client, {"type": "res", "id": id_c, "ok": True, "data": {"val": "c"}})
    await _send_json(client, {"type": "res", "id": id_a, "ok": True, "data": {"val": "a"}})
    await _send_json(client, {"type": "res", "id": id_b, "ok": True, "data": {"val": "b"}})

    # All three waits must resolve to the correct data regardless of order
    result_a, result_b, result_c = await asyncio.gather(
        server.wait_for_response(id_a, timeout=2.0),
        server.wait_for_response(id_b, timeout=2.0),
        server.wait_for_response(id_c, timeout=2.0),
    )

    assert result_a["data"]["val"] == "a"
    assert result_b["data"]["val"] == "b"
    assert result_c["data"]["val"] == "c"


async def test_response_timeout(client, server):
    """
    If no response arrives within the timeout window, wait_for_response should
    either raise asyncio.TimeoutError or return None — never block indefinitely.
    """
    await asyncio.sleep(0.05)

    request_id = await server.send_request("unanswered_method", {})

    # Drain the request frame but intentionally do NOT reply
    await _recv_json(client)

    try:
        result = await server.wait_for_response(request_id, timeout=0.3)
        # Returning None is an acceptable "no response" sentinel
        assert result is None, (
            "wait_for_response should return None (or raise) on timeout, "
            f"but returned: {result!r}"
        )
    except (asyncio.TimeoutError, TimeoutError):
        pass  # raising is equally acceptable


async def test_query_get_inventory(client, server):
    """
    End-to-end simulation of a get_inventory query:
      1. Server sends get_inventory request.
      2. Client (acting as Unity) receives the request and replies with an
         inventory list.
      3. Server's wait_for_response returns the full item list intact.
    """
    await asyncio.sleep(0.05)

    request_id = await server.send_request("get_inventory", {"player_id": "hero"})

    # Unity side: receive the request
    frame = await _recv_json(client)
    assert frame["type"] == "req"
    assert frame["method"] == "get_inventory"
    assert frame["id"] == request_id

    # Unity side: send back a realistic inventory response
    inventory_items = [
        {"id": "item_sword",  "name": "Iron Sword",   "quantity": 1},
        {"id": "item_potion", "name": "Health Potion", "quantity": 3},
        {"id": "item_key",    "name": "Rusty Key",     "quantity": 1},
    ]
    await _send_json(client, {
        "type": "res",
        "id": request_id,
        "ok": True,
        "data": {"items": inventory_items, "capacity": 20},
    })

    # Server side: verify the response was received correctly
    result = await server.wait_for_response(request_id, timeout=2.0)

    assert result is not None
    assert result["ok"] is True
    assert result["data"]["capacity"] == 20

    items = result["data"]["items"]
    assert len(items) == 3

    item_ids = {item["id"] for item in items}
    assert item_ids == {"item_sword", "item_potion", "item_key"}

    sword = next(i for i in items if i["id"] == "item_sword")
    assert sword["name"] == "Iron Sword"
    assert sword["quantity"] == 1
