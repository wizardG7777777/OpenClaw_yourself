"""
tests/playmode/conftest.py

The MockMCPServer runs in a dedicated background thread with its own event loop,
completely independent of pytest-asyncio's per-test loop lifecycle.
"""

from __future__ import annotations

import asyncio
import logging
import threading
import time

import pytest

from mock_server.server import MockMCPServer

logger = logging.getLogger(__name__)

_CONNECT_TIMEOUT: float = 30.0
_CONNECT_POLL: float = 0.5

# ---------------------------------------------------------------------------
# Background server management
# ---------------------------------------------------------------------------

_srv: MockMCPServer | None = None
_loop: asyncio.AbstractEventLoop | None = None
_thread: threading.Thread | None = None


def _run_loop(loop: asyncio.AbstractEventLoop) -> None:
    asyncio.set_event_loop(loop)
    loop.run_forever()


def _start_server() -> MockMCPServer:
    global _srv, _loop, _thread

    if _srv is not None and _srv.is_running:
        return _srv

    _loop = asyncio.new_event_loop()
    _thread = threading.Thread(target=_run_loop, args=(_loop,), daemon=True)
    _thread.start()

    _srv = MockMCPServer()
    future = asyncio.run_coroutine_threadsafe(_srv.start(), _loop)
    future.result(timeout=5)
    logger.info("MockMCPServer started in background thread at %s", _srv.url)
    return _srv


def _stop_server() -> None:
    global _srv, _loop, _thread
    if _srv and _loop:
        future = asyncio.run_coroutine_threadsafe(_srv.stop(), _loop)
        try:
            future.result(timeout=5)
        except Exception:
            pass
        _loop.call_soon_threadsafe(_loop.stop)
        if _thread:
            _thread.join(timeout=5)
    _srv = _loop = _thread = None


def pytest_sessionstart(session: pytest.Session) -> None:
    _start_server()


def pytest_sessionfinish(session: pytest.Session, exitstatus: int) -> None:
    _stop_server()


# ---------------------------------------------------------------------------
# Helper: run async server methods from the test's sync/async context
# ---------------------------------------------------------------------------

def _run_on_server(coro):
    """Submit a coroutine to the server's background loop and wait for result."""
    assert _loop is not None, "Server loop not running"
    future = asyncio.run_coroutine_threadsafe(coro, _loop)
    return future.result(timeout=30)


# ---------------------------------------------------------------------------
# unity_server fixture
# ---------------------------------------------------------------------------


@pytest.fixture
def unity_server():
    """
    Per-test fixture that:
    1. Ensures MockMCPServer is running (background thread)
    2. Clears stored state
    3. Waits for Unity to connect or FAILS with clear error
    4. Yields a wrapper object for interacting with the server
    """
    srv = _start_server()
    srv.clear()

    # Wait for Unity connection (synchronous polling)
    if not srv._connections:
        logger.info("Waiting for Unity to connect (up to %.0fs)...", _CONNECT_TIMEOUT)
        deadline = time.time() + _CONNECT_TIMEOUT
        while time.time() < deadline:
            if srv._connections:
                break
            time.sleep(_CONNECT_POLL)

        if not srv._connections:
            pytest.fail(
                f"Unity did not connect to ws://localhost:8765/ws within {_CONNECT_TIMEOUT:.0f}s.\n"
                "Checklist:\n"
                "  1. Unity Editor is open with mvp_game scene\n"
                "  2. Unity is in Play Mode\n"
                "  3. MCPGateway component has backendUrl = ws://localhost:8765/ws\n"
                "  4. No other process is occupying port 8765"
            )

    yield ServerProxy(srv)


class ServerProxy:
    """
    Synchronous wrapper around MockMCPServer that runs async methods
    on the background thread's event loop.
    """

    def __init__(self, srv: MockMCPServer):
        self._srv = srv

    def send_request(self, method: str, params: dict | None = None) -> str:
        return _run_on_server(self._srv.send_request(method, params))

    def send_event(self, event_name: str, data: dict | None = None) -> None:
        _run_on_server(self._srv.send_event(event_name, data))

    def wait_for_response(self, request_id: str, timeout: float = 5.0) -> dict:
        return _run_on_server(self._srv.wait_for_response(request_id, timeout))

    def wait_for_event(self, event_name: str, timeout: float = 5.0) -> dict:
        return _run_on_server(self._srv.wait_for_event(event_name, timeout))

    def get_responses(self) -> list:
        return self._srv.get_responses()

    def get_events(self) -> list:
        return self._srv.get_events()

    def clear(self) -> None:
        self._srv.clear()

    @property
    def is_connected(self) -> bool:
        return bool(self._srv._connections)
