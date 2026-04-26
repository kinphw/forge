"""HWP Automation API PDF를 txt로 추출.

PyMuPDF 단일 방식. 테이블형 PDF도 열 순서대로 깔끔하게 추출된다.
PUA(사용자 정의 영역, 0xE000–0xF8FF) 글리프는 소량 존재하며 그대로 유지한다.
"""
from __future__ import annotations

import sys
from pathlib import Path

import fitz  # PyMuPDF

BASE = Path(__file__).resolve().parent.parent / "reference" / "official_pdfs"

PDFS = [
    "HwpAutomation_2504.pdf",
    "ActionTable_2504.pdf",
    "ParameterSetTable_2504.pdf",
    "한글오토메이션EventHandler추가_2504.pdf",
]


def extract(pdf_path: Path) -> tuple[str, int]:
    """페이지 단위로 텍스트 추출. PUA 문자 개수도 함께 반환."""
    out: list[str] = []
    pua = 0
    with fitz.open(pdf_path) as doc:
        for i, page in enumerate(doc, start=1):
            text = page.get_text("text")
            pua += sum(1 for c in text if 0xE000 <= ord(c) <= 0xF8FF)
            out.append(f"\n===== Page {i} =====\n")
            out.append(text.rstrip())
    return "\n".join(out).strip() + "\n", pua


def main() -> int:
    if not BASE.exists():
        print(f"[ERROR] 경로 없음: {BASE}", file=sys.stderr)
        return 1

    total_bytes = 0
    for name in PDFS:
        src = BASE / name
        if not src.exists():
            print(f"[SKIP] 파일 없음: {src.name}")
            continue
        dst = src.with_suffix(".txt")
        text, pua = extract(src)
        dst.write_text(text, encoding="utf-8")
        size = dst.stat().st_size
        total_bytes += size
        note = f" (PUA {pua}자)" if pua else ""
        print(f"[ok] {src.name} -> {dst.name}  {size:,} bytes{note}")

    print(f"\n총 출력 크기: {total_bytes:,} bytes ({total_bytes / 1024:.1f} KB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
