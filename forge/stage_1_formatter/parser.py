"""
개조식 markdown 파서.

spec/markdown-spec.md v1.3 기준:
  - YAML front-matter (보고서명·작성부서·작성일)
  - 6단계 본문 층위 (1./가./□/○/-/·)
  - 요약단어 ((요약))
  - 주석 (* 참조 / ※ 일반)
  - 강조 (__X__)
  - 결론 화살표 (=>)
  - Callout 박스 ([참고], [붙임]/[붙임 N])

출력은 시각 spec 무관한 추상 트리 (MarkdownDocument). STAGE 2/3 가
이 트리를 받아 hwpx 로 렌더링.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Optional, Literal

import yaml


# ==========================================================================
# 도메인 모델
# ==========================================================================

NodeType = Literal[
    "section",      # 1. 섹션 헤더
    "subsection",   # 가. 소제목
    "bullet",       # □ ○ - · 본문 글머리
    "annotation",   # * 참조 / ※ 일반 주석
    "conclusion",   # => 결론 화살표
    "callout",      # [참고] / [붙임] 박스
    "blank",        # 빈 줄 (구조 유지용)
]


@dataclass
class Node:
    """파싱된 단락 한 개."""
    type: NodeType
    text: str = ""                    # 글머리 제거 후 본문 텍스트
    marker: Optional[str] = None      # 원본 글머리 ('1.', '가.', '□', '○' 등)
    summary: Optional[str] = None     # □ 라인의 (요약) 부분
    callout_kind: Optional[str] = None  # 'note' (참고) | 'attachment' (붙임)
    callout_number: Optional[int] = None  # [붙임 1], [붙임 2] ...
    annotation_kind: Optional[str] = None  # 'ref' (*/**) | 'general' (※)
    children: list["Node"] = field(default_factory=list)  # callout 내부 본문


@dataclass
class Metadata:
    """YAML front-matter — 3 필드 고정."""
    보고서명: Optional[str] = None
    작성부서: Optional[str] = None
    작성일: Optional[str] = None  # YYYY-MM-DD


@dataclass
class MarkdownDocument:
    """파싱 완료된 md 전체."""
    metadata: Metadata
    nodes: list[Node]


# ==========================================================================
# 파서
# ==========================================================================

FRONT_MATTER_RE = re.compile(r"^---\s*\n(.*?)\n---\s*\n", re.DOTALL)
SECTION_RE = re.compile(r"^(\d+)\.\s+(.+)$")
SUBSECTION_RE = re.compile(r"^([가나다라마바사아자차카타파하])\.\s+(.+)$")
BULLET_RES = [
    # `□`/`○` 는 IME 직접 입력이 까다로움 (한자키 + 선택). 사용자가 손으로 타이핑할
    # 때 자주 쓰는 한글 자모 `ㅁ`(U+3141)/`ㅇ`(U+3147) 도 alias 로 허용. 매칭 후
    # Node.marker 에는 canonical(`□`/`○`) 저장 — 렌더러는 한 종류만 처리.
    ("□", re.compile(r"^[□ㅁ]\s*(.+)$")),
    ("○", re.compile(r"^[○ㅇ]\s*(.+)$")),
    ("-", re.compile(r"^-\s+(.+)$")),
    ("·", re.compile(r"^·\s*(.+)$")),
]
SUMMARY_RE = re.compile(r"^\(([^)]+)\)\s*(.+)$")  # □ (요약) 본문
CONCLUSION_RE = re.compile(r"^=>\s*(.+)$")
ANNOTATION_REF_RE = re.compile(r"^(\*+)\s+(.+)$")
ANNOTATION_GEN_RE = re.compile(r"^[※†]\s*(.+)$")  # 당구장 ※ 또는 십자가 †
CALLOUT_NOTE_RE = re.compile(r"^\[참고\]\s*$")
CALLOUT_ATTACH_RE = re.compile(r"^\[붙임(?:\s+(\d+))?\]\s*$")


def parse_markdown(src: str) -> MarkdownDocument:
    """
    개조식 md 텍스트 → MarkdownDocument.

    파서 동작:
      1. YAML front-matter 추출
      2. 본문을 라인 단위로 스캔
      3. 라인 시작 글머리/마커 패턴 매칭 → Node 생성
      4. callout 시작 마커 만나면 빈 줄까지 children으로 수집
    """
    src = src.lstrip("﻿")  # BOM 제거
    metadata, body = _split_front_matter(src)
    nodes = _parse_body(body)
    return MarkdownDocument(metadata=metadata, nodes=nodes)


def _split_front_matter(src: str) -> tuple[Metadata, str]:
    """YAML front-matter 분리."""
    m = FRONT_MATTER_RE.match(src)
    if not m:
        return Metadata(), src
    try:
        data = yaml.safe_load(m.group(1)) or {}
    except yaml.YAMLError:
        data = {}
    if not isinstance(data, dict):
        data = {}
    metadata = Metadata(
        보고서명=data.get("보고서명"),
        작성부서=data.get("작성부서"),
        작성일=str(data.get("작성일")) if data.get("작성일") is not None else None,
    )
    body = src[m.end():]
    return metadata, body


def _parse_body(body: str) -> list[Node]:
    """본문 라인을 Node 리스트로."""
    nodes: list[Node] = []
    lines = body.splitlines()
    i = 0
    while i < len(lines):
        raw = lines[i]
        line = raw.strip()

        if not line:
            nodes.append(Node(type="blank"))
            i += 1
            continue

        # callout 시작?
        if CALLOUT_NOTE_RE.match(line):
            children, consumed = _parse_callout_body(lines, i + 1)
            nodes.append(Node(type="callout", callout_kind="note", children=children))
            i += 1 + consumed
            continue

        m_attach = CALLOUT_ATTACH_RE.match(line)
        if m_attach:
            num = int(m_attach.group(1)) if m_attach.group(1) else None
            children, consumed = _parse_callout_body(lines, i + 1)
            nodes.append(Node(
                type="callout", callout_kind="attachment",
                callout_number=num, children=children,
            ))
            i += 1 + consumed
            continue

        # 단일 라인 노드들
        node = _parse_single_line(line)
        if node:
            nodes.append(node)
        else:
            # 미식별 라인 — 그냥 텍스트로 (□ 등 마커 없는 본문)
            nodes.append(Node(type="bullet", marker=None, text=line))
        i += 1

    return nodes


def _parse_callout_body(lines: list[str], start: int) -> tuple[list[Node], int]:
    """[참고]/[붙임] 다음 라인부터 빈 줄까지 본문 수집."""
    children: list[Node] = []
    j = start
    while j < len(lines):
        raw = lines[j]
        line = raw.strip()
        if not line:
            break
        # callout 안의 다른 callout / 섹션은 금지 (spec §8) — 만나면 종료
        if CALLOUT_NOTE_RE.match(line) or CALLOUT_ATTACH_RE.match(line):
            break
        if SECTION_RE.match(line):
            break
        node = _parse_single_line(line)
        if node:
            children.append(node)
        else:
            children.append(Node(type="bullet", marker=None, text=line))
        j += 1
    consumed = j - start  # 빈 줄 자체는 외부 루프에서 소비
    return children, consumed


def _parse_single_line(line: str) -> Optional[Node]:
    """라인 하나를 Node로. 매칭 실패 시 None."""
    # 결론 화살표
    m = CONCLUSION_RE.match(line)
    if m:
        return Node(type="conclusion", marker="=>", text=m.group(1))

    # 일반 주석 ※(당구장) 또는 †(십자가)
    m = ANNOTATION_GEN_RE.match(line)
    if m:
        return Node(type="annotation", marker=line[0],  # 입력 마커 보존
                    text=m.group(1), annotation_kind="general")

    # 참조 주석 * (단, "- " 와 충돌 안 함 — 위에 분기됨)
    m = ANNOTATION_REF_RE.match(line)
    if m and not line.startswith("- "):
        return Node(type="annotation", marker=m.group(1),
                    text=m.group(2), annotation_kind="ref")

    # 섹션
    m = SECTION_RE.match(line)
    if m:
        return Node(type="section", marker=f"{m.group(1)}.", text=m.group(2))

    # 소제목
    m = SUBSECTION_RE.match(line)
    if m:
        return Node(type="subsection", marker=f"{m.group(1)}.", text=m.group(2))

    # 본문 글머리
    for marker, pattern in BULLET_RES:
        m = pattern.match(line)
        if m:
            text = m.group(1).strip()
            summary = None
            if marker == "□":
                ms = SUMMARY_RE.match(text)
                if ms:
                    summary, text = ms.group(1), ms.group(2)
            return Node(type="bullet", marker=marker, text=text, summary=summary)

    return None
