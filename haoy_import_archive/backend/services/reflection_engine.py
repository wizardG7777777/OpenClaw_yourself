"""
定期反思引擎（Stanford Generative Agents 风格）。

参考 OpenClaw 的 memory flush 机制和 heartbeat，实现：
  - 活动计时：追踪每个角色的对话次数和累计活动时间
  - 反思触发：每日固定时间 或 累计活动超过阈值 时触发
  - 反思生成：LLM 回顾当日经历，提取 3-5 条洞察写入 MEMORY.md
  - 去重保护：每个反思周期内只触发一次（参考 OpenClaw hasAlreadyFlushedForCurrentCompaction）
"""

from __future__ import annotations

import asyncio
import logging
from datetime import datetime
from typing import Any

from openai import AsyncOpenAI

import config
from services.character_manager import CharacterManager
from services.memory_manager import MemoryManager
from services.memory_files import SECTION_LEARNED

logger = logging.getLogger("services.reflection")

_REFLECTION_PROMPT = """你是「{name}」的记忆反思系统。请回顾今天的经历，提取最重要的3-5条洞察或感悟。

## 今天的经历
{daily_memories}

## 你已有的长期记忆
{long_term_brief}

请从今天的经历中提取值得长期记住的关键信息。重点关注：
1. 关于用户的新信息（近况、喜好变化、重要事件）
2. 自己的情感变化和感悟
3. 值得长期记住的对话内容
4. 与其他角色互动中了解到的新情况

每条洞察独立成行，格式为：
INSIGHT: 具体的洞察内容

如果今天没有值得记录的重要信息，输出：
NO_INSIGHTS"""


class ReflectionEngine:
    """定期反思引擎：追踪活动、触发反思、生成洞察。"""

    def __init__(
        self,
        character_manager: CharacterManager,
        memory_manager: MemoryManager,
    ):
        self.char_mgr = character_manager
        self.mem_mgr = memory_manager
        self._task: asyncio.Task | None = None
        self._running = False
        self.client = AsyncOpenAI(
            api_key=config.LLM_API_KEY or "no-key",
            base_url=config.LLM_BASE_URL,
        )
        # character_id -> 活动状态
        self._activity: dict[str, dict[str, Any]] = {}

    # ── 活动追踪 ────────────────────────────────────

    def record_activity(self, character_id: str, minutes: float = 2.0):
        """记录角色活动（每次对话/互动调用一次）。"""
        state = self._activity.setdefault(character_id, {
            "today_interactions": 0,
            "today_activity_minutes": 0.0,
            "last_reflection_date": "",
        })
        state["today_interactions"] += 1
        state["today_activity_minutes"] += minutes

    def get_activity(self, character_id: str) -> dict:
        return self._activity.get(character_id, {
            "today_interactions": 0,
            "today_activity_minutes": 0.0,
            "last_reflection_date": "",
        })

    # ── 反思触发判断 ────────────────────────────────

    def _should_reflect(self, character_id: str) -> bool:
        state = self._activity.get(character_id)
        if not state:
            return False

        today = datetime.now().strftime("%Y-%m-%d")
        if state.get("last_reflection_date") == today:
            return False

        now = datetime.now()
        if now.hour >= config.REFLECTION_DAILY_HOUR:
            if state["today_interactions"] > 0:
                return True

        if state["today_activity_minutes"] >= config.REFLECTION_ACTIVITY_THRESHOLD_MINUTES:
            return True

        return False

    # ── 后台循环 ────────────────────────────────────

    def start(self):
        if self._running:
            return
        self._running = True
        self._task = asyncio.create_task(self._reflection_loop())
        logger.info(
            "反思引擎已启动 (检查间隔=%ds, 每日时间=%d:00, 活动阈值=%d分钟)",
            config.REFLECTION_CHECK_INTERVAL,
            config.REFLECTION_DAILY_HOUR,
            config.REFLECTION_ACTIVITY_THRESHOLD_MINUTES,
        )

    def stop(self):
        self._running = False
        if self._task:
            self._task.cancel()
            self._task = None

    async def _reflection_loop(self):
        while self._running:
            try:
                await self._check_all()
            except asyncio.CancelledError:
                break
            except Exception as exc:
                logger.exception("反思循环异常: %s", exc)
            await asyncio.sleep(config.REFLECTION_CHECK_INTERVAL)

    async def _check_all(self):
        """检查所有角色是否需要反思。"""
        characters = await self.char_mgr.list_all()
        for char in characters:
            cid = char["id"]
            if self._should_reflect(cid):
                await self.run_reflection(cid, char)

    # ── 执行反思 ────────────────────────────────────

    async def run_reflection(self, character_id: str, char_data: dict | None = None) -> dict | None:
        """
        为角色执行一次反思。
        读取当日经历 -> LLM 生成洞察 -> 写入 MEMORY.md。
        """
        if char_data is None:
            char_data = await self.char_mgr.get(character_id)
        if not char_data:
            return None

        name = char_data["name"]
        today = datetime.now().strftime("%Y-%m-%d")

        daily = self.mem_mgr.get_daily_memory(character_id, today)
        if not daily or len(daily.strip()) < 30:
            logger.info("角色 %s 今天活动不足，跳过反思", name)
            return None

        long_term = self.mem_mgr.get_long_term_memory(character_id)

        prompt = _REFLECTION_PROMPT.format(
            name=name,
            daily_memories=daily[:3000],
            long_term_brief=long_term[:1500] if long_term else "（暂无长期记忆）",
        )

        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=[{"role": "user", "content": prompt}],
                temperature=0.3,
                max_tokens=512,
            )
            import re
            result = re.sub(r"<think>[\s\S]*?</think>\s*", "", response.choices[0].message.content, flags=re.IGNORECASE).strip()

            if "NO_INSIGHTS" in result:
                logger.info("角色 %s 反思完成：今日无重要洞察", name)
                self._mark_reflected(character_id, today)
                return {"character_id": character_id, "insights": 0}

            insights = []
            for line in result.split("\n"):
                line = line.strip()
                if line.startswith("INSIGHT:"):
                    insight = line[len("INSIGHT:"):].strip()
                    if insight:
                        insights.append(insight)

            if insights:
                prefixed = [f"[{today}反思] {ins}" for ins in insights]
                await self.mem_mgr.learn_from_conversation(character_id, prefixed)
                logger.info("角色 %s 反思完成：生成 %d 条洞察", name, len(insights))

            self._mark_reflected(character_id, today)
            self._reset_daily_counters(character_id)

            return {
                "character_id": character_id,
                "insights": len(insights),
                "content": insights,
            }

        except Exception as exc:
            logger.exception("角色 %s 反思失败: %s", name, exc)
            return None

    def _mark_reflected(self, character_id: str, date_str: str):
        state = self._activity.setdefault(character_id, {
            "today_interactions": 0,
            "today_activity_minutes": 0.0,
            "last_reflection_date": "",
        })
        state["last_reflection_date"] = date_str

    def _reset_daily_counters(self, character_id: str):
        state = self._activity.get(character_id)
        if state:
            state["today_interactions"] = 0
            state["today_activity_minutes"] = 0.0
