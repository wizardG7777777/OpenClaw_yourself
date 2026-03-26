"""
集成测试脚本：验证 CyberEternity 后端的 WebSocket 通信和核心功能。

使用方法：
    1. 先启动后端: python main.py
    2. 新开终端运行: python test_integration.py

测试覆盖：
    - WebSocket 连接与心跳
    - 角色创建 / 查询 / 更新 / 删除
    - 记忆添加 / 检索
    - 对话（需要配置 LLM API Key）
    - 场景目标上报
"""

from __future__ import annotations

import asyncio
import json
import sys
import uuid
from typing import Any

import websockets

SERVER_URL = "ws://127.0.0.1:8765/ws"
_counter = 0


def _next_id() -> str:
    global _counter
    _counter += 1
    return f"test_{_counter:04d}"


_pending_events: list[dict] = []


async def send_request(ws, method: str, params: dict | None = None) -> dict:
    req_id = _next_id()
    frame = {
        "type": "req",
        "id": req_id,
        "method": method,
        "params": params or {},
    }
    await ws.send(json.dumps(frame, ensure_ascii=False))
    # 接收帧时跳过事件帧，直到拿到匹配的响应帧
    deadline = asyncio.get_event_loop().time() + 30
    while True:
        remaining = deadline - asyncio.get_event_loop().time()
        if remaining <= 0:
            raise asyncio.TimeoutError(f"等待响应 {req_id} 超时")
        raw = await asyncio.wait_for(ws.recv(), timeout=remaining)
        msg = json.loads(raw)
        if msg.get("type") == "event":
            _pending_events.append(msg)
            continue
        return msg


def assert_ok(res: dict, label: str):
    if not res.get("ok"):
        print(f"  FAIL: {label} — {res.get('error', res)}")
        return False
    print(f"  PASS: {label}")
    return True


async def run_tests():
    print(f"连接到 {SERVER_URL} ...")
    async with websockets.connect(SERVER_URL) as ws:
        print("已连接\n")

        passed = 0
        failed = 0
        total = 0

        def check(ok):
            nonlocal passed, failed, total
            total += 1
            if ok:
                passed += 1
            else:
                failed += 1

        # ── 1. Ping ──────────────────────────────

        print("[1] 心跳测试")
        res = await send_request(ws, "ping")
        check(assert_ok(res, "ping"))

        # ── 2. 创建角色 ──────────────────────────

        print("\n[2] 创建角色")
        res = await send_request(ws, "create_character", {
            "name": "外婆",
            "relationship": "family",
            "personality": "慈祥温暖，喜欢讲故事，总是担心你吃饱了没",
            "backstory": "从小把你带大，最拿手的是红烧肉和糖醋排骨",
            "voice_style": "语气温柔，经常说'乖乖'和'外婆跟你说'",
        })
        check(assert_ok(res, "create_character"))
        # 显示可能收到的事件
        for evt in _pending_events:
            print(f"  收到事件: {evt.get('event')}")
        _pending_events.clear()

        char_id = ""
        if res.get("ok") and res.get("data"):
            char_id = res["data"].get("id", "")
            print(f"  角色ID: {char_id}")

        # ── 3. 查询角色 ──────────────────────────

        print("\n[3] 查询角色")
        res = await send_request(ws, "get_character", {"character_id": char_id})
        check(assert_ok(res, "get_character"))
        if res.get("ok") and res.get("data"):
            print(f"  名称: {res['data'].get('name')}")

        # ── 4. 角色列表 ──────────────────────────

        print("\n[4] 角色列表")
        res = await send_request(ws, "list_characters")
        check(assert_ok(res, "list_characters"))
        if res.get("ok") and res.get("data"):
            count = len(res["data"].get("characters", []))
            print(f"  角色数量: {count}")

        # ── 5. 添加记忆 ──────────────────────────

        print("\n[5] 添加记忆")
        memories_to_add = [
            "小时候每年暑假都去外婆家，她会做我最爱吃的红烧肉",
            "外婆总是在阳台上种满了花，茉莉花开的时候特别香",
            "每次去外婆家，她都会偷偷塞零花钱给我，还叮嘱别告诉妈妈",
            "外婆教我包饺子，虽然我包得丑但她一直夸好看",
        ]
        for mem in memories_to_add:
            res = await send_request(ws, "add_memory", {
                "character_id": char_id,
                "content": mem,
                "importance": 8,
            })
            check(assert_ok(res, f"add_memory: {mem[:15]}..."))

        # ── 6. 检索记忆 ──────────────────────────

        print("\n[6] 检索记忆")
        res = await send_request(ws, "search_memories", {
            "character_id": char_id,
            "query": "外婆做的菜",
        })
        check(assert_ok(res, "search_memories"))
        if res.get("ok") and res.get("data"):
            results = res["data"].get("memories", [])
            print(f"  检索到 {len(results)} 条相关记忆")
            for m in results[:2]:
                print(f"    - [{m.get('memory_type')}] {m['content'][:40]}...")

        # ── 7. 获取所有记忆 ──────────────────────

        print("\n[7] 获取所有记忆")
        res = await send_request(ws, "get_memories", {"character_id": char_id})
        check(assert_ok(res, "get_memories"))
        if res.get("ok") and res.get("data"):
            print(f"  总记忆数: {len(res['data'].get('memories', []))}")

        # ── 8. 对话测试 ──────────────────────────

        print("\n[8] 对话测试（需要 LLM API Key）")
        res = await send_request(ws, "talk_to_character", {
            "character_id": char_id,
            "message": "外婆，你还记得你教我包饺子吗？",
        })
        ok = assert_ok(res, "talk_to_character")
        check(ok)
        if ok and res.get("data"):
            print(f"  回复: {res['data'].get('reply', '')}")
            print(f"  情感: {res['data'].get('emotion', '')}")

        # ── 9. 上报场景目标 ──────────────────────

        print("\n[9] 上报场景目标")
        res = await send_request(ws, "update_scene_targets", {
            "targets": [
                {"entity_id": "sofa_01", "display_name": "沙发"},
                {"entity_id": "tv_01", "display_name": "电视机"},
                {"entity_id": "kitchen_table", "display_name": "厨房餐桌"},
                {"entity_id": "balcony_flowers", "display_name": "阳台花架"},
            ]
        })
        check(assert_ok(res, "update_scene_targets"))

        # ── 10. 更新角色 ─────────────────────────

        print("\n[10] 更新角色")
        res = await send_request(ws, "update_character", {
            "character_id": char_id,
            "personality": "慈祥温暖，喜欢讲故事，总是担心你吃饱了没。最近学会了用手机发语音。",
        })
        check(assert_ok(res, "update_character"))

        # ── 11. 删除角色 ─────────────────────────

        print("\n[11] 删除角色")
        res = await send_request(ws, "delete_character", {"character_id": char_id})
        check(assert_ok(res, "delete_character"))
        _pending_events.clear()

        # ── 12. 未知方法测试 ─────────────────────

        print("\n[12] 未知方法测试")
        res = await send_request(ws, "nonexistent_method")
        is_fail = not res.get("ok") and "UNKNOWN_METHOD" in str(res.get("error", ""))
        total += 1
        if is_fail:
            passed += 1
            print("  PASS: 正确返回 UNKNOWN_METHOD 错误")
        else:
            failed += 1
            print(f"  FAIL: 预期错误，实际: {res}")

        # ── 结果汇总 ─────────────────────────────

        print(f"\n{'='*50}")
        print(f"测试完成: {passed}/{total} 通过, {failed} 失败")
        print(f"{'='*50}")
        return failed == 0


if __name__ == "__main__":
    success = asyncio.run(run_tests())
    sys.exit(0 if success else 1)
