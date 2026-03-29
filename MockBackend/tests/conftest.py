"""
tests/conftest.py — Root conftest. Marker registration only.
Playmode-specific fixtures are in tests/playmode/conftest.py.
"""

import pytest


def pytest_configure(config: pytest.Config) -> None:
    config.addinivalue_line(
        "markers",
        "playmode: tests that require Unity Play Mode",
    )
