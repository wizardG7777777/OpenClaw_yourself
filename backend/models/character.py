import json
from datetime import datetime, timezone

from sqlalchemy import String, Text, Float, DateTime
from sqlalchemy.orm import Mapped, mapped_column

from models.database import Base


class Character(Base):
    __tablename__ = "characters"

    id: Mapped[str] = mapped_column(String(64), primary_key=True)
    name: Mapped[str] = mapped_column(String(128), nullable=False)
    relationship: Mapped[str] = mapped_column(String(32), default="other")
    personality: Mapped[str] = mapped_column(Text, default="")
    appearance: Mapped[str] = mapped_column(Text, default="")
    backstory: Mapped[str] = mapped_column(Text, default="")
    voice_style: Mapped[str] = mapped_column(Text, default="")
    status: Mapped[str] = mapped_column(String(32), default="idle")
    # 位置以 JSON 字符串存储 {"x": 0, "y": 0, "z": 0}
    current_position: Mapped[str] = mapped_column(Text, default='{"x":0,"y":0,"z":0}')
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=lambda: datetime.now(timezone.utc)
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime,
        default=lambda: datetime.now(timezone.utc),
        onupdate=lambda: datetime.now(timezone.utc),
    )

    def to_dict(self) -> dict:
        return {
            "id": self.id,
            "name": self.name,
            "relationship": self.relationship,
            "personality": self.personality,
            "appearance": self.appearance,
            "backstory": self.backstory,
            "voice_style": self.voice_style,
            "status": self.status,
            "current_position": json.loads(self.current_position),
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }
