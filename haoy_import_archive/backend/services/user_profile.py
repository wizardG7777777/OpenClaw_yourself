"""
全局用户档案管理（USER_PROFILE.md）。

所有角色共享同一个用户档案，确保每个角色都"认识"用户。
档案只存储客观事实，不存储角色个人对用户的情感。

参考 OpenClaw 的 USER.md 和 Claw3D 的 personalityBuilder 中 draft.user.context。
"""

from __future__ import annotations

import re
import logging
from pathlib import Path

import config

logger = logging.getLogger("services.user_profile")

_PROFILE_PATH = config.BASE_DIR / "data" / "USER_PROFILE.md"

SECTION_BASIC = "基本信息"
SECTION_SHARED_FACTS = "共享事实"

_SECTION_RE = re.compile(r"^##\s+(.+)$", re.MULTILINE)


def _ensure_dir():
    _PROFILE_PATH.parent.mkdir(parents=True, exist_ok=True)


def init_user_profile(nickname: str, basic_info: str = "") -> str:
    """首次创建全局用户档案。"""
    _ensure_dir()

    info_lines = []
    if nickname:
        info_lines.append(f"- 昵称：{nickname}")
    info_lines.append("- 身份：这个家庭的核心成员")
    if basic_info:
        for line in basic_info.strip().split("\n"):
            line = line.strip()
            if line and not line.startswith("- "):
                line = f"- {line}"
            if line:
                info_lines.append(line)

    content = f"""# 用户档案

## {SECTION_BASIC}
{chr(10).join(info_lines)}

## {SECTION_SHARED_FACTS}
（由各角色对话中自动汇总）
"""
    _PROFILE_PATH.write_text(content, encoding="utf-8")
    logger.info("用户档案已创建: %s", _PROFILE_PATH)
    return content


def read_user_profile() -> str:
    """读取用户档案全文。不存在返回空串。"""
    if not _PROFILE_PATH.exists():
        return ""
    return _PROFILE_PATH.read_text(encoding="utf-8")


def update_user_profile(content: str) -> bool:
    """覆写用户档案（前端 UI 编辑）。"""
    _ensure_dir()
    _PROFILE_PATH.write_text(content, encoding="utf-8")
    return True


def append_shared_fact(fact: str) -> bool:
    """向「共享事实」区段追加一条新事实。自动去重。"""
    if not _PROFILE_PATH.exists():
        return False

    content = _PROFILE_PATH.read_text(encoding="utf-8")
    fact = fact.strip()
    if not fact:
        return False

    if fact in content:
        return False

    if not fact.startswith("- "):
        fact = f"- {fact}"

    pattern = re.compile(
        rf"(## {re.escape(SECTION_SHARED_FACTS)}\n)(.*?)(\n## |\Z)",
        re.DOTALL,
    )
    match = pattern.search(content)
    if not match:
        content += f"\n## {SECTION_SHARED_FACTS}\n{fact}\n"
    else:
        header = match.group(1)
        body = match.group(2).rstrip()
        tail = match.group(3)

        if "（由各角色" in body:
            body = ""

        new_body = f"{body}\n{fact}" if body else fact
        content = content[:match.start()] + header + new_body + "\n" + tail + content[match.end():]

    _PROFILE_PATH.write_text(content, encoding="utf-8")
    logger.info("用户档案新增共享事实: %s", fact[:40])
    return True


def profile_exists() -> bool:
    return _PROFILE_PATH.exists()
