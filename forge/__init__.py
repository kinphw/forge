"""Forge core library — UI/CLI 모두에서 공유."""

# ★★★ 단일 진실원본 (single source of truth) — 다른 곳에 하드코드 금지.
# pyproject.toml 도 [tool.setuptools.dynamic] 로 forge.__version__ 을 참조.
# UI 의 윈도우 타이틀·푸터·About 다이얼로그·CLI 진입점도 모두 여기서 import.
__version__ = "0.2.3"
__author__ = "kinphw"
__app_name__ = "Forge"
__tagline__ = "github.com/kinphw/forge"

__all__ = ["__version__", "__author__", "__app_name__", "__tagline__"]
