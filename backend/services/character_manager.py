"""
角色管理服务：创建、查询、更新、删除角色。
"""

from __future__ import annotations

import json
import uuid
import logging
from typing import Any

from sqlalchemy import select, delete
from sqlalchemy.ext.asyncio import AsyncSession

from models.database import async_session
from models.character import Character

logger = logging.getLogger("services.character")


def _new_id() -> str:
    return f"char_{uuid.uuid4().hex[:8]}"


class CharacterManager:
    """角色 CRUD 服务。所有方法均为异步，内部管理数据库会话。"""

    async def create(self, data: dict[str, Any]) -> dict:
        char = Character(
            id=_new_id(),
            name=data.get("name", "未命名"),
            relationship=data.get("relationship", "other"),
            personality=data.get("personality", ""),
            appearance=data.get("appearance", ""),
            backstory=data.get("backstory", ""),
            voice_style=data.get("voice_style", ""),
            status="idle",
            current_position=json.dumps(data.get("position", {"x": 0, "y": 0, "z": 0})),
        )
        async with async_session() as session:
            session.add(char)
            await session.commit()
            await session.refresh(char)
            logger.info("角色已创建: %s (%s)", char.name, char.id)
            return char.to_dict()

    async def get(self, character_id: str) -> dict | None:
        async with async_session() as session:
            char = await session.get(Character, character_id)
            return char.to_dict() if char else None

    async def list_all(self) -> list[dict]:
        async with async_session() as session:
            result = await session.execute(select(Character).order_by(Character.created_at.desc()))
            return [c.to_dict() for c in result.scalars().all()]

    async def update(self, character_id: str, data: dict[str, Any]) -> dict | None:
        async with async_session() as session:
            char = await session.get(Character, character_id)
            if char is None:
                return None
            updatable = [
                "name", "relationship", "personality", "appearance",
                "backstory", "voice_style", "status",
            ]
            for key in updatable:
                if key in data:
                    setattr(char, key, data[key])
            if "position" in data:
                char.current_position = json.dumps(data["position"])
            await session.commit()
            await session.refresh(char)
            logger.info("角色已更新: %s", character_id)
            return char.to_dict()

    async def delete(self, character_id: str) -> bool:
        async with async_session() as session:
            result = await session.execute(
                delete(Character).where(Character.id == character_id)
            )
            await session.commit()
            deleted = result.rowcount > 0
            if deleted:
                logger.info("角色已删除: %s", character_id)
            return deleted

    async def update_position(self, character_id: str, position: dict) -> bool:
        async with async_session() as session:
            char = await session.get(Character, character_id)
            if char is None:
                return False
            char.current_position = json.dumps(position)
            await session.commit()
            return True

    async def update_status(self, character_id: str, status: str) -> bool:
        async with async_session() as session:
            char = await session.get(Character, character_id)
            if char is None:
                return False
            char.status = status
            await session.commit()
            return True
