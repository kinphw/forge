"""STAGE 1 — md → hwpx 변환."""
from .templates import REPORT1_SPEC, ReportSpec
from .parser import MarkdownDocument, parse_markdown
from .hwpx_writer import generate_hwpx_via_com

__all__ = [
    "REPORT1_SPEC", "ReportSpec",
    "MarkdownDocument", "parse_markdown",
    "generate_hwpx_via_com",
]
