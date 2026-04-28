"""
Forge GUI 진입점.

실행:
    python run.py

또는 (pyproject.toml 설치 후):
    sentinel-forge
"""
import sys

from ui.app import main

if __name__ == "__main__":
    sys.exit(main())
