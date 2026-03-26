"""
WebSocket 帧协议定义。

三种帧类型（参考 Claw3D GatewayClient + Unity MCPRequest/MCPResponse）：
  - req:   请求帧，携带 method + params
  - res:   响应帧，携带 ok + data/error
  - event: 事件帧，后端主动推送
"""

from __future__ import annotations

import json
import uuid
from dataclasses import dataclass, field, asdict
from typing import Any


def _short_id() -> str:
    return uuid.uuid4().hex[:8]


# ── 请求帧 ──────────────────────────────────────────────

@dataclass
class ReqFrame:
    method: str
    params: dict[str, Any] = field(default_factory=dict)
    id: str = field(default_factory=_short_id)
    type: str = field(default="req", init=False)


# ── 响应帧 ──────────────────────────────────────────────

@dataclass
class ResFrame:
    id: str
    ok: bool
    data: Any = None
    error: dict[str, Any] | None = None
    type: str = field(default="res", init=False)

    @staticmethod
    def success(request_id: str, data: Any = None) -> "ResFrame":
        return ResFrame(id=request_id, ok=True, data=data)

    @staticmethod
    def fail(request_id: str, code: str, message: str, *, retryable: bool = False) -> "ResFrame":
        return ResFrame(
            id=request_id,
            ok=False,
            error={"code": code, "message": message, "retryable": retryable},
        )


# ── 事件帧 ──────────────────────────────────────────────

@dataclass
class EventFrame:
    event: str
    data: dict[str, Any] = field(default_factory=dict)
    type: str = field(default="event", init=False)


# ── 序列化 / 反序列化 ──────────────────────────────────

def frame_to_json(frame: ReqFrame | ResFrame | EventFrame) -> str:
    d = asdict(frame)
    if d.get("data") is None:
        d.pop("data", None)
    if d.get("error") is None:
        d.pop("error", None)
    return json.dumps(d, ensure_ascii=False)


def parse_frame(raw: str) -> dict[str, Any] | None:
    """将原始 JSON 字符串解析为字典；解析失败返回 None。"""
    try:
        return json.loads(raw)
    except (json.JSONDecodeError, TypeError):
        return None
