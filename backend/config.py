import os
from pathlib import Path
from dotenv import load_dotenv

BASE_DIR = Path(__file__).resolve().parent
load_dotenv(BASE_DIR / ".env")

# LLM
LLM_API_KEY = os.getenv("LLM_API_KEY", "")
LLM_BASE_URL = os.getenv("LLM_BASE_URL", "https://api.openai.com/v1")
LLM_MODEL = os.getenv("LLM_MODEL", "gpt-4o-mini")

# 服务
HOST = os.getenv("HOST", "127.0.0.1")
WS_PORT = int(os.getenv("WS_PORT", "8765"))

# 数据库
DB_PATH = BASE_DIR / os.getenv("DB_PATH", "data/cybereternity.db")
DATABASE_URL = f"sqlite+aiosqlite:///{DB_PATH}"

# 角色行为
AUTO_BEHAVIOR_INTERVAL = int(os.getenv("AUTO_BEHAVIOR_INTERVAL", "15"))

# 记忆
MAX_CORE_MEMORIES_PER_QUERY = int(os.getenv("MAX_CORE_MEMORIES_PER_QUERY", "5"))
MAX_CONVERSATION_HISTORY = int(os.getenv("MAX_CONVERSATION_HISTORY", "10"))
