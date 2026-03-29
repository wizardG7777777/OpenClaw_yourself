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

# 记忆系统 v2
MEMORY_AUTO_LEARN = os.getenv("MEMORY_AUTO_LEARN", "true").lower() == "true"
MEMORY_DAILY_INJECT_DAYS = int(os.getenv("MEMORY_DAILY_INJECT_DAYS", "2"))
MEMORY_FTS_MIN_SCORE = float(os.getenv("MEMORY_FTS_MIN_SCORE", "0.05"))

# 角色间互动
CHARACTER_INTERACTION_INTERVAL = int(os.getenv("CHARACTER_INTERACTION_INTERVAL", "60"))
CHARACTER_INTERACTION_PROBABILITY = float(os.getenv("CHARACTER_INTERACTION_PROBABILITY", "0.5"))

# 定期反思（Stanford Generative Agents 风格）
REFLECTION_CHECK_INTERVAL = int(os.getenv("REFLECTION_CHECK_INTERVAL", "300"))
REFLECTION_ACTIVITY_THRESHOLD_MINUTES = int(os.getenv("REFLECTION_ACTIVITY_THRESHOLD_MINUTES", "120"))
REFLECTION_DAILY_HOUR = int(os.getenv("REFLECTION_DAILY_HOUR", "22"))

# 全局用户档案
USER_PROFILE_ENABLED = os.getenv("USER_PROFILE_ENABLED", "true").lower() == "true"
