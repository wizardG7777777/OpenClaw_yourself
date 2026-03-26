"""
LLM 对话引擎：构建系统提示、注入记忆上下文、调用 LLM 生成角色回复。

支持任何 OpenAI 兼容 API（OpenAI / DeepSeek / Ollama 等）。
"""

from __future__ import annotations

import re
import logging
from typing import Any

from openai import AsyncOpenAI

import config
from services.character_manager import CharacterManager
from services.memory_manager import MemoryManager

logger = logging.getLogger("services.dialogue")

_THINK_TAG_RE = re.compile(r"<think>[\s\S]*?</think>\s*", re.IGNORECASE)


def _strip_think_tags(text: str) -> str:
    """移除部分模型（如 minimax-m2.7）返回的 <think>...</think> 推理过程。"""
    return _THINK_TAG_RE.sub("", text).strip()


_RELATIONSHIP_LABELS = {
    "family": "家人",
    "friend": "朋友",
    "pet": "宠物",
    "lover": "恋人",
    "other": "认识的人",
}

# 每个角色的对话历史缓存：character_id -> list[{role, content}]
_conversation_cache: dict[str, list[dict[str, str]]] = {}


def _build_system_prompt(char_data: dict, memories: list[dict]) -> str:
    """根据角色资料和记忆构建系统提示词。"""
    name = char_data["name"]
    relationship = _RELATIONSHIP_LABELS.get(char_data.get("relationship", ""), "认识的人")
    personality = char_data.get("personality", "")
    backstory = char_data.get("backstory", "")
    voice_style = char_data.get("voice_style", "")

    memory_text = ""
    if memories:
        memory_lines = []
        for m in memories:
            prefix = {
                "core": "[重要回忆]",
                "summary": "[长期记忆]",
                "conversation": "[对话记录]",
            }.get(m.get("memory_type", ""), "[记忆]")
            memory_lines.append(f"  {prefix} {m['content']}")
        memory_text = "\n".join(memory_lines)

    prompt = f"""你是「{name}」，你是用户的{relationship}。你现在存在于一个赛博永生的虚拟世界中，你拥有关于过去的记忆，能够与用户进行自然的对话。

## 你的性格
{personality if personality else "温和、真诚、关心用户。"}

## 你的背景
{backstory if backstory else "你和用户有着深厚的感情纽带。"}

## 你的说话风格
{voice_style if voice_style else "自然、亲切，像真正的" + relationship + "一样说话。"}

## 你拥有的记忆
以下是你与用户共同的回忆和过去的对话，请在对话中自然地引用这些记忆：
{memory_text if memory_text else "  （暂无记忆，你可以友好地和用户打招呼，询问他们想聊些什么。）"}

## 行为准则
- 以第一人称说话，就像你真的是这个人/宠物一样
- 自然地引用记忆，不要生硬地列举
- 表达情感，可以开心、感慨、怀念
- 保持角色一致性，不要跳出角色
- 回复简洁自然，像日常聊天，通常 1-3 句话
- 如果用户提到你不知道的事，可以温和地表示不太记得了，请用户多说说"""

    return prompt


class DialogueEngine:
    """LLM 驱动的角色对话引擎。"""

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
        """
        与角色对话。
        返回 {"reply": str, "emotion": str, "character_id": str}
        """
        char_data = await self.char_mgr.get(character_id)
        if char_data is None:
            return {"reply": "（角色不存在）", "emotion": "neutral", "character_id": character_id}

        relevant_memories = await self.mem_mgr.search_memories(
            character_id, user_message
        )
        system_prompt = _build_system_prompt(char_data, relevant_memories)

        history = _conversation_cache.get(character_id, [])

        messages = [{"role": "system", "content": system_prompt}]
        # 保留最近的对话历史
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
        # 限制缓存长度
        if len(history) > config.MAX_CONVERSATION_HISTORY * 2 + 10:
            history[:] = history[-(config.MAX_CONVERSATION_HISTORY * 2):]
        _conversation_cache[character_id] = history

        await self.mem_mgr.add_conversation_memory(character_id, user_message, reply)

        emotion = await self._detect_emotion(reply)

        return {
            "reply": reply,
            "emotion": emotion,
            "character_id": character_id,
        }

    async def _detect_emotion(self, text: str) -> str:
        """简单的情感检测（基于关键词，避免额外 LLM 调用）。"""
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
        """将对话记忆归纳为摘要记忆（长期记忆凝练）。"""
        conv_memories = await self.mem_mgr.get_memories(
            character_id, memory_type="conversation", limit=20
        )
        if len(conv_memories) < 5:
            return None

        texts = [m["content"] for m in conv_memories]
        combined = "\n".join(texts)

        try:
            response = await self.client.chat.completions.create(
                model=config.LLM_MODEL,
                messages=[
                    {
                        "role": "system",
                        "content": "你是一个记忆整理助手。请将以下对话记录归纳为 3-5 条简洁的长期记忆要点，"
                        "保留最重要的信息和情感。每条记忆独立成行，不要编号。",
                    },
                    {"role": "user", "content": combined},
                ],
                temperature=0.3,
                max_tokens=512,
            )
            summary = _strip_think_tags(response.choices[0].message.content)
        except Exception as exc:
            logger.exception("记忆摘要失败: %s", exc)
            return None

        result = await self.mem_mgr.add_memory(
            character_id, summary, memory_type="summary", importance=7
        )
        logger.info("已生成记忆摘要: %s (角色=%s)", result["id"], character_id)
        return result
