"""
mock_server/server.py

A mock WebSocket server that simulates the MCP backend for Unity integration tests.

The server listens on ws://localhost:8765/ws (matching Unity's default backendUrl) and
speaks the same frame protocol as MCPGateway.cs:

  req   — server -> Unity  {"type":"req",   "id":"<id>", "method":"<m>", "params":{}}
  res   — Unity  -> server {"type":"res",   "id":"<id>", "ok":true,      "data":{}}
  event — either direction {"type":"event", "event":"<name>",            "data":{}}
"""

from __future__ import annotations

import asyncio
import json
import logging
import uuid
from collections import defaultdict
from typing import Any

import websockets
from websockets.asyncio.server import ServerConnection, serve

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Frame helpers
# ---------------------------------------------------------------------------

def _make_request(method: str, params: dict[str, Any], request_id: str) -> str:
    return json.dumps({"type": "req", "id": request_id, "method": method, "params": params})


def _make_event(event_name: str, data: dict[str, Any]) -> str:
    return json.dumps({"type": "event", "event": event_name, "data": data})


# ---------------------------------------------------------------------------
# MockMCPServer
# ---------------------------------------------------------------------------

class MockMCPServer:
    """
    Async mock WebSocket server that mirrors the MCP backend expected by Unity.

    Typical pytest usage::

        @pytest.fixture
        async def server():
            async with MockMCPServer() as srv:
                yield srv

        async def test_something(server):
            req_id = await server.send_request("get_inventory", {})
            response = await server.wait_for_response(req_id, timeout=5.0)
            assert response["ok"] is True
    """

    DEFAULT_HOST: str = "localhost"
    DEFAULT_PORT: int = 8765
    DEFAULT_PATH: str = "/ws"

    def __init__(
        self,
        host: str = DEFAULT_HOST,
        port: int = DEFAULT_PORT,
        path: str = DEFAULT_PATH,
    ) -> None:
        self._host = host
        self._port = port
        self._path = path

        # Active WebSocket connections (usually just one during a test).
        self._connections: set[ServerConnection] = set()

        # Received frames, keyed by frame type.
        self._responses: list[dict[str, Any]] = []
        self._events: list[dict[str, Any]] = []

        # Pending waiters: request_id -> Future that resolves to the response frame.
        self._response_waiters: dict[str, asyncio.Future[dict[str, Any]]] = {}

        # Pending event waiters: event_name -> list of Futures.
        self._event_waiters: dict[str, list[asyncio.Future[dict[str, Any]]]] = defaultdict(list)

        # Internal server handle returned by websockets.serve().
        self._server: websockets.asyncio.server.Server | None = None

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    async def start(self) -> None:
        """Start listening. Idempotent if already started."""
        if self._server is not None:
            return

        self._server = await serve(
            self._connection_handler,
            self._host,
            self._port,
        )
        logger.info("MockMCPServer listening on ws://%s:%d%s", self._host, self._port, self._path)

    async def stop(self) -> None:
        """Stop the server and close all active connections."""
        if self._server is None:
            return

        self._server.close()
        await self._server.wait_closed()
        self._server = None
        logger.info("MockMCPServer stopped")

    # Async context manager support -------------------------------------

    async def __aenter__(self) -> "MockMCPServer":
        await self.start()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        await self.stop()

    # ------------------------------------------------------------------
    # Internal: connection and message handling
    # ------------------------------------------------------------------

    async def _connection_handler(self, ws: ServerConnection) -> None:
        """Called by websockets for every new client connection."""
        # Optionally enforce the path so only /ws is accepted.
        if ws.request and ws.request.path != self._path:
            await ws.close(1008, f"Only {self._path} is supported")
            return

        self._connections.add(ws)
        logger.debug("Unity connected: %s", ws.remote_address)

        try:
            async for raw in ws:
                await self._handle_frame(raw)
        except websockets.exceptions.ConnectionClosedOK:
            pass
        except websockets.exceptions.ConnectionClosedError as exc:
            logger.warning("Connection closed with error: %s", exc)
        finally:
            self._connections.discard(ws)
            logger.debug("Unity disconnected: %s", ws.remote_address)

    async def _handle_frame(self, raw: str | bytes) -> None:
        """Parse an incoming frame and dispatch to the appropriate store / waiter."""
        try:
            frame: dict[str, Any] = json.loads(raw)
        except json.JSONDecodeError:
            logger.warning("Received non-JSON frame: %r", raw)
            return

        frame_type = frame.get("type")

        if frame_type == "res":
            self._responses.append(frame)
            request_id: str | None = frame.get("id")
            if request_id and request_id in self._response_waiters:
                future = self._response_waiters.pop(request_id)
                if not future.done():
                    future.set_result(frame)

        elif frame_type == "event":
            self._events.append(frame)
            event_name: str | None = frame.get("event")
            if event_name and event_name in self._event_waiters:
                waiters = self._event_waiters[event_name]
                if waiters:
                    future = waiters.pop(0)
                    if not future.done():
                        future.set_result(frame)

        else:
            logger.warning("Unknown frame type %r: %s", frame_type, frame)

    # ------------------------------------------------------------------
    # Sending frames to Unity
    # ------------------------------------------------------------------

    async def send_request(self, method: str, params: dict[str, Any] | None = None) -> str:
        """
        Send a ``type: "req"`` frame to all connected Unity clients.

        Returns the auto-generated request ID so the caller can later call
        ``wait_for_response(request_id)``.
        """
        request_id = f"req_{uuid.uuid4().hex[:8]}"
        payload = _make_request(method, params or {}, request_id)
        await self._broadcast(payload)
        logger.debug("Sent request id=%s method=%s", request_id, method)
        return request_id

    async def send_event(self, event_name: str, data: dict[str, Any] | None = None) -> None:
        """Send a ``type: "event"`` frame to all connected Unity clients."""
        payload = _make_event(event_name, data or {})
        await self._broadcast(payload)
        logger.debug("Sent event event=%s", event_name)

    async def _broadcast(self, payload: str) -> None:
        """Send *payload* to every currently-connected client."""
        if not self._connections:
            logger.warning("No connected clients; frame dropped: %s", payload)
            return
        # websockets.broadcast is synchronous in v13; use it when available.
        websockets.broadcast(self._connections, payload)

    # ------------------------------------------------------------------
    # Waiting for frames from Unity
    # ------------------------------------------------------------------

    async def wait_for_response(
        self,
        request_id: str,
        timeout: float = 5.0,
    ) -> dict[str, Any]:
        """
        Async-wait until Unity sends a ``type: "res"`` frame with the given *request_id*.

        Raises ``asyncio.TimeoutError`` if no matching response arrives within *timeout*
        seconds.

        If the response was already received before this method is called it is
        returned immediately from the store.
        """
        # Check if it has already arrived.
        for response in self._responses:
            if response.get("id") == request_id:
                return response

        loop = asyncio.get_event_loop()
        future: asyncio.Future[dict[str, Any]] = loop.create_future()
        self._response_waiters[request_id] = future
        try:
            return await asyncio.wait_for(future, timeout=timeout)
        except asyncio.TimeoutError:
            self._response_waiters.pop(request_id, None)
            raise

    async def wait_for_event(
        self,
        event_name: str,
        timeout: float = 5.0,
    ) -> dict[str, Any]:
        """
        Async-wait until Unity sends a ``type: "event"`` frame with the given *event_name*.

        Raises ``asyncio.TimeoutError`` if no matching event arrives within *timeout*
        seconds.

        If a matching event was already received it is returned immediately.
        """
        # Check if already received (return the first match).
        for event in self._events:
            if event.get("event") == event_name:
                return event

        loop = asyncio.get_event_loop()
        future: asyncio.Future[dict[str, Any]] = loop.create_future()
        self._event_waiters[event_name].append(future)
        try:
            return await asyncio.wait_for(future, timeout=timeout)
        except asyncio.TimeoutError:
            waiters = self._event_waiters.get(event_name, [])
            if future in waiters:
                waiters.remove(future)
            raise

    # ------------------------------------------------------------------
    # Introspection helpers
    # ------------------------------------------------------------------

    def get_responses(self) -> list[dict[str, Any]]:
        """Return a snapshot of all ``type: "res"`` frames received from Unity."""
        return list(self._responses)

    def get_events(self) -> list[dict[str, Any]]:
        """Return a snapshot of all ``type: "event"`` frames received from Unity."""
        return list(self._events)

    def clear(self) -> None:
        """Clear all stored frames and pending waiters. Useful between test cases."""
        self._responses.clear()
        self._events.clear()
        self._response_waiters.clear()
        self._event_waiters.clear()

    # ------------------------------------------------------------------
    # Convenience properties
    # ------------------------------------------------------------------

    @property
    def url(self) -> str:
        """The WebSocket URL this server is listening on."""
        return f"ws://{self._host}:{self._port}{self._path}"

    @property
    def is_running(self) -> bool:
        """True if the underlying websockets server is active."""
        return self._server is not None
