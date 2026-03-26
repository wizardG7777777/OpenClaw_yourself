"""
Markdown 文件记忆存储层。

参考 OpenClaw 的双层 Markdown 设计：
  - MEMORY.md     长期记忆（分区结构化，角色创建时初始化）
  - memory/YYYY-MM-DD.md  每日记忆（带时间戳，对话时追加）

每个角色一个目录：data/characters/{character_id}/
"""

from __future__ import annotations

import os
import re
import logging
from datetime import datetime, timezone, timedelta
from pathlib import Path
from typing import Any

import config

logger = logging.getLogger("services.memory_files")

CHARACTERS_DIR = config.BASE_DIR / "data" / "characters"

# MEMORY.md 中的标准区段名
SECTION_ABOUT_USER = "关于用户"
SECTION_PREFERENCES = "我的喜好与特征"
SECTION_EVENTS = "重要事件"
SECTION_LEARNED = "从对话中学到的新信息"

_SECTION_RE = re.compile(r"^##\s+(.+)$", re.MULTILINE)


def _char_dir(character_id: str) -> Path:
    return CHARACTERS_DIR / character_id


def _memory_dir(character_id: str) -> Path:
    return _char_dir(character_id) / "memory"


def _memory_md_path(character_id: str) -> Path:
    return _char_dir(character_id) / "MEMORY.md"


def _daily_path(character_id: str, date_str: str | None = None) -> Path:
    if date_str is None:
        date_str = datetime.now().strftime("%Y-%m-%d")
    return _memory_dir(character_id) / f"{date_str}.md"


def _now_time_label() -> str:
    """返回中文友好的当前时间标签，如 '下午3:15'。"""
    now = datetime.now()
    hour = now.hour
    minute = now.minute
    if hour < 6:
        period = "凌晨"
    elif hour < 12:
        period = "上午"
    elif hour == 12:
        period = "中午"
    elif hour < 18:
        period = "下午"
    else:
        period = "晚上"
    display_hour = hour if hour <= 12 else hour - 12
    return f"{period}{display_hour}:{minute:02d}"


# ══════════════════════════════════════════════════════
#  MEMORY.md 长期记忆操作
# ══════════════════════════════════════════════════════

def init_memory_md(character_id: str, char_data: dict) -> str:
    """
    根据角色资料初始化 MEMORY.md。
    返回生成的 Markdown 文本。
    """
    name = char_data.get("name", "")
    personality = char_data.get("personality", "")
    backstory = char_data.get("backstory", "")

    about_user_lines = []
    if backstory:
        for line in backstory.split("\n"):
            line = line.strip()
            if line:
                about_user_lines.append(f"- {line}")
    if not about_user_lines:
        about_user_lines.append("- （暂无信息）")

    pref_lines = []
    if personality:
        for line in personality.split("\n"):
            line = line.strip()
            if line:
                pref_lines.append(f"- {line}")
    if not pref_lines:
        pref_lines.append("- （暂无信息）")

    content = f"""# {name}的长期记忆

## {SECTION_ABOUT_USER}
{chr(10).join(about_user_lines)}

## {SECTION_PREFERENCES}
{chr(10).join(pref_lines)}

## {SECTION_EVENTS}
- （暂无记录）

## {SECTION_LEARNED}
（此部分由AI在对话过程中自动追加更新）
"""

    path = _memory_md_path(character_id)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    _memory_dir(character_id).mkdir(parents=True, exist_ok=True)
    logger.info("已初始化 MEMORY.md: %s", path)
    return content


def read_memory_md(character_id: str) -> str:
    """读取 MEMORY.md 全文。文件不存在返回空串。"""
    path = _memory_md_path(character_id)
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8")


def parse_sections(content: str) -> dict[str, str]:
    """
    解析 MEMORY.md 为 {区段名: 区段内容} 字典。
    """
    sections: dict[str, str] = {}
    parts = _SECTION_RE.split(content)
    # parts: [前导文本, 区段名1, 区段内容1, 区段名2, 区段内容2, ...]
    for i in range(1, len(parts) - 1, 2):
        section_name = parts[i].strip()
        section_body = parts[i + 1].strip()
        sections[section_name] = section_body
    return sections


def append_to_section(character_id: str, section_name: str, lines: list[str]) -> bool:
    """
    向 MEMORY.md 指定区段末尾追加内容。
    每行自动加 '- ' 前缀（如果没有的话）。
    """
    path = _memory_md_path(character_id)
    if not path.exists():
        return False

    content = path.read_text(encoding="utf-8")

    formatted = []
    for line in lines:
        line = line.strip()
        if not line:
            continue
        if not line.startswith("- "):
            line = f"- {line}"
        formatted.append(line)

    if not formatted:
        return False

    insert_text = "\n".join(formatted)

    pattern = re.compile(
        rf"(## {re.escape(section_name)}\n)(.*?)(\n## |\Z)",
        re.DOTALL,
    )
    match = pattern.search(content)
    if not match:
        content += f"\n## {section_name}\n{insert_text}\n"
    else:
        header = match.group(1)
        body = match.group(2).rstrip()
        tail = match.group(3)

        placeholder_patterns = ["（暂无", "（此部分由"]
        cleaned_lines = []
        for existing_line in body.split("\n"):
            if any(p in existing_line for p in placeholder_patterns):
                continue
            cleaned_lines.append(existing_line)
        cleaned_body = "\n".join(cleaned_lines).rstrip()

        new_body = f"{cleaned_body}\n{insert_text}" if cleaned_body else insert_text
        content = content[:match.start()] + header + new_body + "\n" + tail + content[match.end():]

    path.write_text(content, encoding="utf-8")
    logger.info("已向 %s 的 [%s] 区段追加 %d 行", character_id, section_name, len(formatted))
    return True


def update_memory_md(character_id: str, new_content: str) -> bool:
    """用新内容完整覆写 MEMORY.md（供前端编辑功能使用）。"""
    path = _memory_md_path(character_id)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(new_content, encoding="utf-8")
    return True


# ══════════════════════════════════════════════════════
#  每日记忆文件操作
# ══════════════════════════════════════════════════════

def append_daily_memory(character_id: str, entries: list[str], date_str: str | None = None) -> str:
    """
    向当日记忆文件追加条目。
    返回写入的文件路径（相对于角色目录）。
    """
    if date_str is None:
        date_str = datetime.now().strftime("%Y-%m-%d")

    path = _daily_path(character_id, date_str)
    path.parent.mkdir(parents=True, exist_ok=True)

    time_label = _now_time_label()

    formatted = []
    for entry in entries:
        entry = entry.strip()
        if entry and not entry.startswith("- "):
            entry = f"- {entry}"
        if entry:
            formatted.append(entry)

    if not formatted:
        return str(path.relative_to(_char_dir(character_id)))

    block = f"\n## {time_label}\n" + "\n".join(formatted) + "\n"

    if not path.exists():
        header = f"# {date_str}\n"
        path.write_text(header + block, encoding="utf-8")
    else:
        with open(path, "a", encoding="utf-8") as f:
            f.write(block)

    return str(path.relative_to(_char_dir(character_id)))


def read_daily_memory(character_id: str, date_str: str) -> str:
    """读取指定日期的记忆文件。不存在返回空串。"""
    path = _daily_path(character_id, date_str)
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8")


def read_recent_daily_memories(character_id: str, days: int = 2) -> str:
    """
    读取最近 N 天的每日记忆，合并返回。
    参考 OpenClaw 的「今天 + 昨天」注入逻辑。
    """
    parts = []
    today = datetime.now()
    for i in range(days):
        date = today - timedelta(days=i)
        date_str = date.strftime("%Y-%m-%d")
        content = read_daily_memory(character_id, date_str)
        if content.strip():
            parts.append(content)
    return "\n\n".join(parts)


def list_daily_files(character_id: str) -> list[dict]:
    """列出该角色所有的每日记忆文件（按日期倒序）。"""
    mem_dir = _memory_dir(character_id)
    if not mem_dir.exists():
        return []
    files = []
    for f in sorted(mem_dir.glob("*.md"), reverse=True):
        stat = f.stat()
        files.append({
            "date": f.stem,
            "filename": f.name,
            "size_bytes": stat.st_size,
        })
    return files


def list_all_memory_files(character_id: str) -> list[Path]:
    """列出角色目录下所有 .md 文件（MEMORY.md + 每日文件），用于索引。"""
    char_path = _char_dir(character_id)
    if not char_path.exists():
        return []
    result = []
    memory_md = _memory_md_path(character_id)
    if memory_md.exists():
        result.append(memory_md)
    mem_dir = _memory_dir(character_id)
    if mem_dir.exists():
        result.extend(sorted(mem_dir.glob("*.md")))
    return result


def delete_character_files(character_id: str):
    """删除角色的整个文件目录。"""
    import shutil
    char_path = _char_dir(character_id)
    if char_path.exists():
        shutil.rmtree(char_path)
        logger.info("已删除角色文件目录: %s", char_path)
