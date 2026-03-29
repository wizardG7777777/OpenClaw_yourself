import sqlite3
from pathlib import Path

from sqlalchemy import text
from sqlalchemy.ext.asyncio import create_async_engine, async_sessionmaker, AsyncSession
from sqlalchemy.orm import DeclarativeBase

from config import DATABASE_URL, DB_PATH


class Base(DeclarativeBase):
    pass


engine = create_async_engine(DATABASE_URL, echo=False)
async_session = async_sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)


async def init_db():
    """创建所有 ORM 表和 FTS5 虚拟表。"""
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await _create_fts_tables(conn)


async def _create_fts_tables(conn):
    """创建 FTS5 全文检索虚拟表（参考 OpenClaw manager.ts 的 SQLite schema）。"""
    await conn.execute(text("""
        CREATE TABLE IF NOT EXISTS memory_chunks (
            chunk_id   TEXT PRIMARY KEY,
            character_id TEXT NOT NULL,
            source_path  TEXT NOT NULL,
            section      TEXT DEFAULT '',
            content      TEXT NOT NULL,
            content_tokenized TEXT NOT NULL,
            start_line   INTEGER DEFAULT 0,
            end_line     INTEGER DEFAULT 0,
            created_at   TEXT DEFAULT (datetime('now'))
        )
    """))
    await conn.execute(text("""
        CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
            content_tokenized,
            content='memory_chunks',
            content_rowid='rowid'
        )
    """))
    await conn.execute(text("""
        CREATE TRIGGER IF NOT EXISTS memory_chunks_ai AFTER INSERT ON memory_chunks BEGIN
            INSERT INTO memory_fts(rowid, content_tokenized)
            VALUES (new.rowid, new.content_tokenized);
        END
    """))
    await conn.execute(text("""
        CREATE TRIGGER IF NOT EXISTS memory_chunks_ad AFTER DELETE ON memory_chunks BEGIN
            INSERT INTO memory_fts(memory_fts, rowid, content_tokenized)
            VALUES ('delete', old.rowid, old.content_tokenized);
        END
    """))
    await conn.execute(text("""
        CREATE TRIGGER IF NOT EXISTS memory_chunks_au AFTER UPDATE ON memory_chunks BEGIN
            INSERT INTO memory_fts(memory_fts, rowid, content_tokenized)
            VALUES ('delete', old.rowid, old.content_tokenized);
            INSERT INTO memory_fts(rowid, content_tokenized)
            VALUES (new.rowid, new.content_tokenized);
        END
    """))


async def get_session() -> AsyncSession:
    async with async_session() as session:
        yield session
