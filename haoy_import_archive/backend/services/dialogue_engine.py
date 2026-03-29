"""
LLM 对话引擎（v3）：基于两层 Markdown 记忆 + 全局用户档案 + 反思追踪。

记忆注入方式：
  1. USER_PROFILE.md → 系统提示中的「你认识的用户」段（所有角色共享）
  2. MEMORY.md 完整内容 → 系统提示中的「长期记忆」段
  3. 近 N 天的每日记忆文件 → 系统提示中的「近期记忆」段
  4. FTS5 BM25 搜索结果 → 系统提示中的「相关回忆片段」段
"""

from __future__ import annotations

import re
import logging
from typing import Any

from openai import AsyncOpenAI

import config
from services.character_manager import CharacterManager
from services.memory_manager import MemoryManager
from services.user_profile import read_user_profile, append_shared_fact

logger = logging.getLogger("services.dialogue")

_THINK_TAG_RE = re.compile(r"<think>[\s\S]*?</think>\s*", re.IGNORECASE)


def _strip_think_tags(text: str) -> str:
    return _THINK_TAG_RE.sub("", text).strip()


_RELATIONSHIP_LABELS = {
    "family": "家人",
    "friend": "朋友",
    "pet": "宠物",
    "lover": "恋人",
    "other": "认识的人",
}

_conversation_cache: dict[str, list[dict[str, str]]] = {}

# 反思引擎引用（由 main.py 注入，避免循环导入）
_reflection_engine = None


def set_reflection_engine(engine):
    global _reflection_engine
    _reflection_engine = engine


def _build_system_prompt(
    char_data: dict,
    user_profile: str,
    long_term_memory: str,
    recent_memory: str,
    search_snippets: list[dict],
) -> str:
    name = char_data["name"]
    relationship = _RELATIONSHIP_LABELS.get(char_data.get("relationship", ""), "认识的人")
    personality = char_data.get("personality", "")
    voice_style = char_data.get("voice_style", "")

    snippets_text = ""
    if search_snippets:
        lines = []
        for s in search_snippets[:5]:
            source = s.get("source_path", "")
            lines.append(f"  [{source}] {s['content'][:200]}")
        snippets_text = "\n".join(lines)

    prompt = f"""你是「{name}」，你是用户的{relationship}。你现在存在于一个赛博永生的虚拟世界中。

## 你的性格
{personality if personality else "温和、真诚、关心用户。"}

## 你的说话风格
{voice_style if voice_style else "自然、亲切，像真正的" + relationship + "一样说话。"}"""

    if user_profile.strip():
        prompt += f"""

## 你认识的用户
{user_profile}"""

    prompt += f"""

## 你的长期记忆
以下是你记住的关于自己和用户的重要信息：
{long_term_memory if long_term_memory.strip() else "（暂无长期记忆）"}

## 近期记忆
以下是最近几天发生的事：
{recent_memory if recent_memory.strip() else "（最近没有特别的事）"}"""

    if snippets_text:
        prompt += f"""

## 与当前话题相关的回忆片段
{snippets_text}"""

    prompt += """

## 行为准则
- 以第一人称说话，就像你真的是这个人/宠物一样
- 自然地引用记忆，不要生硬地列举
- 表达情感，可以开心、感慨、怀念
- 保持角色一致性，不要跳出角色
- 回复简洁自然，像日常聊天，通常 1-3 句话
- 如果用户提到你不知道的事，可以温和地表示不太记得了，请用户多说说"""

    return prompt


_LEARN_PROMPT = """你是一个信息提取助手。请分析以下对话，判断是否包含值得长期记住的**新事实信息**。

新事实信息的标准：
- 关于用户的具体新信息（工作变动、搬家、新爱好、健康状况等）
- 重要事件或计划（生日、旅行、考试等）
- 用户明确表达的偏好或观点变化
- 不包括已经在长期记忆中存在的信息
- 不包括闲聊、问候、模糊的情感表达

当前长期记忆中已有的信息：
{existing_memory}

本轮对话：
用户: {user_msg}
角色: {char_reply}

如果有值得记住的新信息，请用以下格式输出（每条一行，不要编号）：
NEW_FACT: 具体的新信息
USER_FACT: 关于用户的新信息（此类信息会同步到所有角色共享的用户档案）

如果没有新信息，输出：
NO_NEW_FACTS"""


class DialogueEngine:
    """LLM 驱动的角色对话引擎（v3：含用户档案 + 反思追踪）。"""

    def __init__(
        self,
        character_manager: CharacterManager,
        memory_manager: MemoryManager,
    ):
        self.char_mgr = character_manager
        self.mem_mgr = memory_manager
        self.client = AsyncOpenAI(
            api_key=config.LLM_API_KEY or "no-key",
            base_url=config.LLM_BASE_URL,
        )

    async def chat(self, character_id: str, user_message: str) -> dict[str, Any]:
        char_data = await self.char_mgr.get(character_id)
        if char_data is None:
            return {"reply": "（角色不存在）", "emotion": "neutral", "character_id": character_id}

        user_profile = read_user_profile()
        long_term = self.mem_mgr.get_long_term_memory(character_id)
        recent = self.mem_mgr.get_recent_memories(character_id)
        search_results = await self.mem_mgr.search_memories(character_id, user_message)

        system_prompt = _build_system_prompt(
            char_data, user_profile, long_term, recent, search_results
        )

        history = _conversation_cache.get(character_id, [])
        messages = [{"role": "system", "content": system_prompt}]
        tail = history[-(config.MAX_CONVERSATION_HISTORY * 2):]
        messages.extend(tail)
        messages.append({"role": "user", "content": user_message})

        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=messages,
                temperature=0.85,
                max_tokens=512,
            )
            reply = _strip_think_tags(response.choices[0].message.content)
        except Exception as exc:
            logger.exception("LLM 调用失败: %s", exc)
            reply = f"（对话系统暂时无法响应：{exc}）"

        history.append({"role": "user", "content": user_message})
        history.append({"role": "assistant", "content": reply})
        if len(history) > config.MAX_CONVERSATION_HISTORY * 2 + 10:
            history[:] = history[-(config.MAX_CONVERSATION_HISTORY * 2):]
        _conversation_cache[character_id] = history

        await self.mem_mgr.add_conversation_memory(character_id, user_message, reply)

        # 追踪活动（供反思引擎使用）
        if _reflection_engine:
            _reflection_engine.record_activity(character_id, minutes=2.0)

        if config.MEMORY_AUTO_LEARN:
            await self._learn_from_conversation(character_id, user_message, reply, long_term)

        emotion = self._detect_emotion(reply)
        return {"reply": reply, "emotion": emotion, "character_id": character_id}

    async def _learn_from_conversation(
        self,
        character_id: str,
        user_msg: str,
        char_reply: str,
        existing_memory: str,
    ):
        prompt = _LEARN_PROMPT.format(
            existing_memory=existing_memory[:2000] if existing_memory else "（空）",
            user_msg=user_msg,
            char_reply=char_reply,
        )
        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=[{"role": "user", "content": prompt}],
                temperature=0.1,
                max_tokens=256,
            )
            result = _strip_think_tags(response.choices[0].message.content)

            if "NO_NEW_FACTS" in result:
                return

            new_facts = []
            user_facts = []
            for line in result.split("\n"):
                line = line.strip()
                if line.startswith("USER_FACT:"):
                    fact = line[len("USER_FACT:"):].strip()
                    if fact:
                        new_facts.append(fact)
                        user_facts.append(fact)
                elif line.startswith("NEW_FACT:"):
                    fact = line[len("NEW_FACT:"):].strip()
                    if fact:
                        new_facts.append(fact)

            if new_facts:
                await self.mem_mgr.learn_from_conversation(character_id, new_facts)
                logger.info("角色 %s 学到 %d 条新信息", character_id, len(new_facts))

            # 同步到全局用户档案
            if user_facts and config.USER_PROFILE_ENABLED:
                for uf in user_facts:
                    append_shared_fact(uf)
                logger.info("已同步 %d 条用户事实到全局档案", len(user_facts))

        except Exception as exc:
            logger.warning("自动学习失败（不影响对话）: %s", exc)

    def _detect_emotion(self, text: str) -> str:
        positive = {"开心", "高兴", "快乐", "哈哈", "好的", "太好了", "喜欢", "爱", "想念", "怀念", "感动"}
        negative = {"难过", "伤心", "抱歉", "对不起", "遗憾", "可惜", "不好意思"}
        nostalgic = {"记得", "那时候", "以前", "从前", "回忆", "当年", "小时候", "过去"}

        for word in nostalgic:
            if word in text:
                return "nostalgic"
        for word in positive:
            if word in text:
                return "happy"
        for word in negative:
            if word in text:
                return "sad"
        return "neutral"

    async def summarize_memories(self, character_id: str) -> dict | None:
        recent = self.mem_mgr.get_recent_memories(character_id, days=7)
        if not recent.strip() or len(recent) < 100:
            return None

        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=[
                    {
                        "role": "system",
                        "content": "你是一个记忆整理助手。请将以下几天的对话记录归纳为重要的长期记忆要点。"
                        "每条要点独立成行，格式为「- 要点内容」。只保留值得长期记住的关键信息。",
                    },
                    {"role": "user", "content": recent},
                ],
                temperature=0.3,
                max_tokens=512,
            )
            summary = _strip_think_tags(response.choices[0].message.content)
        except Exception as exc:
            logger.exception("记忆摘要失败: %s", exc)
            return None

        facts = [l.strip().lstrip("- ").strip() for l in summary.split("\n") if l.strip().startswith("-")]
        if facts:
            await self.mem_mgr.learn_from_conversation(character_id, facts)

        return {"character_id": character_id, "summarized_facts": len(facts), "content": summary}
