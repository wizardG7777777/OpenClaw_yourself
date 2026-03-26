"""
WebSocket 通信桥：管理连接、解析帧、路由到对应的业务处理器。
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any, Callable, Awaitable

from fastapi import WebSocket, WebSocketDisconnect

from ws.protocol import ResFrame, EventFrame, frame_to_json, parse_frame

logger = logging.getLogger("ws.bridge")

# method -> async handler(ws_manager, connection_id, params) -> response_data
MethodHandler = Callable[["WebSocketManager", str, dict[str, Any]], Awaitable[Any]]


class WebSocketManager:
    """管理所有 WebSocket 连接，提供帧路由与事件广播。"""

    def __init__(self):
        self._connections: dict[str, WebSocket] = {}
        self._handlers: dict[str, MethodHandler] = {}
        self._heartbeat_interval = 30  # 秒

    # ── 连接管理 ────────────────────────────────────────

    async def connect(self, connection_id: str, ws: WebSocket):
        await ws.accept()
        self._connections[connection_id] = ws
        logger.info("客户端已连接: %s (当前连接数: %d)", connection_id, len(self._connections))

    def disconnect(self, connection_id: str):
        self._connections.pop(connection_id, None)
        logger.info("客户端已断开: %s (当前连接数: %d)", connection_id, len(self._connections))

    # ── Handler 注册 ────────────────────────────────────

    def register(self, method: str, handler: MethodHandler):
        self._handlers[method] = handler

    # ── 发送 ────────────────────────────────────────────

    async def send_to(self, connection_id: str, frame: ResFrame | EventFrame):
        ws = self._connections.get(connection_id)
        if ws:
            try:
                await ws.send_text(frame_to_json(frame))
            except Exception:
                logger.warning("发送失败，移除连接: %s", connection_id)
                self.disconnect(connection_id)

    async def broadcast_event(self, event: EventFrame):
        dead: list[str] = []
        text = frame_to_json(event)
        for cid, ws in self._connections.items():
            try:
                await ws.send_text(text)
            except Exception:
                dead.append(cid)
        for cid in dead:
            self.disconnect(cid)

    # ── 主循环：接收帧 → 路由 → 响应 ──────────────────

    async def listen(self, connection_id: str, ws: WebSocket):
        try:
            while True:
                raw = await ws.receive_text()
                frame = parse_frame(raw)
                if frame is None:
                    await self.send_to(
                        connection_id,
                        ResFrame.fail("unknown", "PARSE_ERROR", "无法解析 JSON 帧"),
                    )
                    continue

                frame_type = frame.get("type")
                if frame_type == "req":
                    await self._handle_request(connection_id, frame)
                elif frame_type == "event":
                    await self._handle_incoming_event(connection_id, frame)
                else:
                    req_id = frame.get("id", "unknown")
                    await self.send_to(
                        connection_id,
                        ResFrame.fail(req_id, "UNKNOWN_FRAME_TYPE", f"未知帧类型: {frame_type}"),
                    )
        except WebSocketDisconnect:
            pass
        except Exception as exc:
            logger.exception("WebSocket 监听异常: %s", exc)
        finally:
            self.disconnect(connection_id)

    async def _handle_request(self, connection_id: str, frame: dict):
        req_id = frame.get("id", "unknown")
        method = frame.get("method", "")
        params = frame.get("params") or {}

        handler = self._handlers.get(method)
        if handler is None:
            await self.send_to(
                connection_id,
                ResFrame.fail(req_id, "UNKNOWN_METHOD", f"未注册的方法: {method}"),
            )
            return

        try:
            result = await handler(self, connection_id, params)
            await self.send_to(connection_id, ResFrame.success(req_id, result))
        except Exception as exc:
            logger.exception("处理请求 %s 失败: %s", method, exc)
            await self.send_to(
                connection_id,
                ResFrame.fail(req_id, "INTERNAL_ERROR", str(exc)),
            )

    async def _handle_incoming_event(self, connection_id: str, frame: dict):
        """处理 Unity 主动上报的事件（如移动完成）。"""
        event_name = frame.get("event", "")
        handler = self._handlers.get(f"event:{event_name}")
        if handler:
            data = frame.get("data") or {}
            try:
                await handler(self, connection_id, data)
            except Exception as exc:
                logger.exception("处理事件 %s 失败: %s", event_name, exc)
