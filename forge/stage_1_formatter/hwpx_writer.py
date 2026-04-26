"""
MarkdownDocument → .hwpx 파일 생성 (또는 활성 문서 커서 위치 삽입).

본 모듈은 **dispatcher** 역할만 한다 — 실제 시각 렌더링은 forge/renderers/
의 각 ElementRenderer 클래스가 담당.

mode:
  - "new"    : 한/글에서 FileNew → 페이지 여백 → 메타데이터 헤더 → 본문 → SaveAs
  - "cursor" : 활성 문서·커서 위치 그대로 사용. 본문만 삽입. 저장은 사용자가 한/글에서.

spec v1.4 (2026-04-26): 메타데이터의 보고서명만 markdown front-matter에서 옴.
작성부서·작성일은 caller(UI)가 별도로 추가 인자로 넘김.
"""
from __future__ import annotations

import os
from typing import Any, Optional

from ..com_helpers import set_param
from ..renderers import (
    AnnotationRenderer,
    AttachmentRenderer,
    BulletRenderer,
    ConclusionRenderer,
    MetadataRenderer,
    NoteCalloutRenderer,
    SectionRenderer,
    SubsectionRenderer,
)
from ..renderers import primitives as p
from ..stage_2_linter import (
    adjust_kerning_to_avoid_word_break,
    align_left_indent,
)
from .parser import MarkdownDocument, Node
from .templates import REPORT1_SPEC, ReportSpec


def generate_hwpx_via_com(
    hwp: Any,
    doc: MarkdownDocument,
    out_path: str,
    spec: ReportSpec = REPORT1_SPEC,
    log: callable = print,
    mode: str = "new",
    작성부서: Optional[str] = None,
    작성일:   Optional[str] = None,
    is_new_session: bool = False,
    apply_indent_align: bool = True,
    apply_kerning: bool = True,
) -> str:
    """
    Args:
        hwp:      살아있는 한/글 COM 인스턴스 (HwpSession.hwp)
        doc:      파싱된 markdown
        out_path: 저장할 .hwpx 절대 경로 ("new" 모드만)
        spec:     보고서 양식 spec
        log:      진행 상황 콜백
        mode:     "new" | "cursor"
        작성부서: spec v1.4 — md 가 아닌 UI 입력에서 옴
        작성일:   spec v1.4 — UI 입력에서 옴 (YYYY-MM-DD)
        is_new_session:
            True 면 한/글 인스턴스를 우리가 방금 띄운 상태 — 자동 생성된
            빈 문서를 그대로 활용해 FileNew 를 생략. False(기존 attach)
            면 사용자 작업 문서 보존을 위해 FileNew 로 분리.
        apply_indent_align:
            True (default) — STAGE 2 들여쓰기 정렬 후처리 실행 (bullet/
            annotation 라인의 marker 다음 위치를 left indent 로 정렬).
            "new" 모드에서만 동작. cursor 모드는 사용자 기존 문서 뒤에
            추가 삽입하는 시나리오를 보호하기 위해 skip — 전체 순회 시
            사용자가 미리 작성한 부분의 자간·들여쓰기까지 건드리게 됨.
            (신규 삽입 영역만 인식해 적용하는 변형은 후속 작업.)
        apply_kerning:
            True (default) — STAGE 2 자간조정 후처리 실행 (어절 잘림 방지).
            apply_indent_align 과 동일하게 "new" 모드에서만 동작.

    Returns:
        "new" 모드: 저장된 파일 절대 경로
        "cursor" 모드: 빈 문자열
    """
    # ─── 모드별 사전 설정 ───────────────────────────
    if mode == "new":
        out_path = os.path.abspath(out_path)
        if is_new_session:
            log("[STAGE 1] 신규 한/글 — 자동 생성된 빈 문서 활용 (FileNew 생략)")
        else:
            log("[STAGE 1] 기존 한/글에 attach — 새 문서 분리 (FileNew)")
            p.run(hwp, "FileNew")

        log(f"[STAGE 1] 페이지 여백 적용: L={spec.margins.left} R={spec.margins.right} "
            f"T={spec.margins.top} B={spec.margins.bottom} mm")
        p.set_page_margins(
            hwp,
            left_mm=spec.margins.left, right_mm=spec.margins.right,
            top_mm=spec.margins.top, bottom_mm=spec.margins.bottom,
            header_mm=spec.margins.header, footer_mm=spec.margins.footer,
        )

        # 메타데이터 헤더 (보고서명 노란박스 + 부서·일자 stamp)
        meta = doc.metadata
        if meta.보고서명 or 작성부서 or 작성일:
            log(f"[STAGE 1] 메타데이터 헤더: 보고서명={meta.보고서명!r} "
                f"부서={작성부서!r} 일자={작성일!r}")
            MetadataRenderer(hwp, spec).render(
                보고서명=meta.보고서명,
                작성부서=작성부서,
                작성일=작성일,
            )
    else:
        # cursor 모드 — 현재 커서 위치 그대로
        log("[STAGE 1] 활성 문서 커서 위치에 본문 삽입 (페이지·메타데이터 미변경)")

    # ─── 본문 노드 dispatcher ───────────────────────
    log(f"[STAGE 1] 본문 {len(doc.nodes)} 노드 dispatcher 시작")
    _dispatch_nodes(hwp, doc.nodes, spec, log)
    # (Bold 인라인 토큰 `__X__` 은 primitives.insert_text 가 렌더링 시점에 처리)

    # ─── STAGE 2 후처리 (new 모드만) ─────────────────
    # 순서: 자간조정 → 들여쓰기 정렬.
    # 사용자 검증 (2026-04-27): 들여쓰기 먼저 하면 자간조정으로 글자 너비
    # 변경 후 본문 시작 위치가 미세하게 달라져 정렬이 틀어짐. 자간을 먼저
    # 확정한 뒤 들여쓰기 정렬해야 본문 첫 글자 위치가 정확히 잡힘.
    # cursor 모드는 skip — 기존 문서 뒤 추가 시나리오 보호.
    if mode == "new":
        if apply_kerning:
            log("[STAGE 2] 자간조정 (어절 잘림 방지, 줄당 ±15회)")
            try:
                adjust_kerning_to_avoid_word_break(hwp)
            except Exception as e:
                log(f"  ⚠ 자간조정 중단: {e}")
        if apply_indent_align:
            log("[STAGE 2] 들여쓰기 정렬 (bullet/annotation 라인)")
            try:
                align_left_indent(hwp)
            except Exception as e:
                log(f"  ⚠ 들여쓰기 정렬 중단: {e}")

    # ─── 모드별 저장 ────────────────────────────────
    if mode == "new":
        log(f"[STAGE 1] hwpx 저장: {out_path}")
        _save_as_hwpx(hwp, out_path)
        log("[STAGE 1] ✔ 완료")
        return out_path
    else:
        log("[STAGE 1] ✔ 커서 위치 삽입 완료 (저장은 한/글에서 직접)")
        return ""


# ============================================================================
# 노드 → 렌더러 dispatcher
# ============================================================================

# md 글머리 → bullet level 매핑
_BULLET_LEVELS = {"□": 1, "○": 2, "-": 3, "·": 4}


def _dispatch_nodes(
    hwp: Any,
    nodes: list[Node],
    spec: ReportSpec,
    log: callable,
) -> None:
    """노드 리스트 순회 — 타입에 따라 적절한 렌더러 호출."""
    for node in nodes:
        try:
            _dispatch_one(hwp, node, spec)
        except Exception as e:
            log(f"  ✘ 노드 렌더링 실패 ({node.type} marker={node.marker!r}): {e}")
            # 한 노드 실패해도 나머지 진행
            try:
                p.break_para(hwp)
            except Exception:
                pass


def _dispatch_one(hwp: Any, node: Node, spec: ReportSpec) -> None:
    """한 노드 → 적절한 렌더러 1회 호출."""
    if node.type == "blank":
        p.break_para(hwp)
        return

    if node.type == "section":
        # marker '1.' → int 1
        try:
            num = int(node.marker.rstrip("."))
        except (ValueError, AttributeError):
            num = 0
        SectionRenderer(hwp, spec).render(num, node.text)
        return

    if node.type == "subsection":
        # marker '가.' → '가'
        marker = node.marker.rstrip(".") if node.marker else ""
        SubsectionRenderer(hwp, spec).render(marker, node.text)
        return

    if node.type == "bullet":
        level = _BULLET_LEVELS.get(node.marker)
        if level is None:
            # 마커 없는 본문 — 그냥 텍스트
            p.insert_text(hwp, node.text)
            p.break_para(hwp)
            return
        BulletRenderer(hwp, spec).render(
            level=level,
            body=node.text,
            summary=node.summary,
        )
        return

    if node.type == "annotation":
        marker = node.marker or "*"
        AnnotationRenderer(hwp, spec).render(marker, node.text)
        return

    if node.type == "conclusion":
        ConclusionRenderer(hwp, spec).render(node.text)
        return

    if node.type == "callout":
        # children 의 텍스트만 모아서 lines 로
        lines = [c.text for c in node.children if c.text]
        if node.callout_kind == "note":
            NoteCalloutRenderer(hwp, spec).render(lines)
        else:
            AttachmentRenderer(hwp, spec).render(node.callout_number, lines)
        return

    # 알 수 없는 타입 — 그냥 텍스트
    if node.text:
        p.insert_text(hwp, node.text)
        p.break_para(hwp)


# ============================================================================
# 저장
# ============================================================================

def _save_as_hwpx(hwp: Any, out_path: str) -> None:
    """SaveAs .hwpx — Windows 경로 형식, format='HWPX' 우선 시도."""
    out_path = out_path.replace("/", "\\")
    try:
        hwp.SaveAs(out_path, "HWPX", "")
    except TypeError:
        hwp.SaveAs(out_path)
