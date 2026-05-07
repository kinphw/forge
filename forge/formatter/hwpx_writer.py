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
from ..linter import (
    adjust_kerning_to_avoid_word_break,
    align_left_indent,
)
from ..linter._range import selection_range
from .parser import MarkdownDocument, Node, parse_markdown
from .templates import REPORT1_SPEC, ReportSpec


class NoSelectionError(RuntimeError):
    """convert_selection_to_hwpx 호출 시 selection 영역이 설정되지 않은 상태."""


def generate_hwpx_via_com(
    hwp: Any,
    doc: MarkdownDocument,
    out_path: Optional[str] = None,
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
        out_path: 저장할 .hwpx 절대 경로 ("new" 모드 + 즉시 저장 시).
                  None/빈 문자열 → 저장 단계 skip — caller 가 변환 후 직접
                  save_as_hwpx() 호출 (사용자가 결과를 보고 파일명을 지정하는
                  지연-저장 패턴).
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
        "new" 모드 + out_path 지정: 저장된 파일 절대 경로
        "new" 모드 + out_path None : 빈 문자열 (저장 미수행)
        "cursor" 모드               : 빈 문자열
    """
    # ─── 모드별 사전 설정 ───────────────────────────
    if mode == "new":
        out_path = os.path.abspath(out_path) if out_path else ""
        # 운영 정책: 사용자가 한/글을 먼저 띄운 상태에서만 작동 (allow_spawn=False).
        # → 항상 기존 attach 케이스. Run("FileNew") 로 새 문서 분리.
        # XHwpDocuments.Add() 는 doc 객체는 생기지만 HAction cursor target 이
        # 이전 doc 에 머무는 부작용 있음 (사용자 검증 2026-04-27) — 사용 금지.
        log("[STAGE 1] 새 문서 생성 (FileNew)")
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
        metadata_emitted = False
        if meta.보고서명 or 작성부서 or 작성일:
            log(f"[STAGE 1] 메타데이터 헤더: 보고서명={meta.보고서명!r} "
                f"부서={작성부서!r} 일자={작성일!r}")
            MetadataRenderer(hwp, spec).render(
                보고서명=meta.보고서명,
                작성부서=작성부서,
                작성일=작성일,
            )
            metadata_emitted = True
    else:
        # cursor 모드 — 현재 커서 위치 그대로
        log("[STAGE 1] 활성 문서 커서 위치에 본문 삽입 (페이지·메타데이터 미변경)")
        metadata_emitted = False

    # ─── 본문 노드 dispatcher ───────────────────────
    # 메타데이터 헤더가 emit 되었으면 본문 첫 노드 앞에 1줄 prepend.
    # cursor 모드면 사용자 작업 문서 흐름 보존 위해 첫 노드 앞 자동 prepend 안 함.
    log(f"[STAGE 1] 본문 {len(doc.nodes)} 노드 dispatcher 시작")
    _dispatch_nodes(
        hwp, doc.nodes, spec, log,
        initial_prev_emitted=metadata_emitted,
    )
    # (Bold 인라인 토큰 `__X__` 은 primitives.insert_text 가 렌더링 시점에 처리)

    # ─── STAGE 2 후처리 (new 모드만) ─────────────────
    # 순서: 들여쓰기 → 자간 → 들여쓰기 (3단계). hotkey Q '자동 정렬' 과 동일.
    #
    # 사용자 검증 (2026-05-06, v0.2.2 hotkey Q):
    #   - 인덴트 0 상태에서 자간 → 인덴트 적용하면 wrap 점이 옆으로 밀려
    #     자간 보정값이 무의미해짐 (자간 효과 죽음).
    #   - 인덴트 → 자간 만 하면 자간 후 본문 위치 미세 drift 로 인덴트가
    #     살짝 어긋남.
    #   - 1차 인덴트로 wrap 기준 고정 → 자간 보정 → 2차 인덴트로 drift
    #     보정 — 두 인덴트는 역할이 다른 별개 호출.
    #
    # 직전(2026-04-27) 의 '자간 → 인덴트' 결론은 hotkey Q 갱신으로 폐기.
    # 'md 변환은 개별 작업(realtime_tab) 의 권위 결론을 따른다' 정책.
    # cursor 모드는 skip — 기존 문서 뒤 추가 시나리오 보호.
    if mode == "new":
        if apply_indent_align:
            log("[STAGE 2] (1/3) 들여쓰기 정렬 — wrap 기준 확정")
            try:
                align_left_indent(hwp)
            except Exception as e:
                log(f"  ⚠ 1차 들여쓰기 정렬 중단: {e}")
        if apply_kerning:
            log("[STAGE 2] (2/3) 자간조정 — 어절 잘림 방지 (줄당 ±15회)")
            try:
                adjust_kerning_to_avoid_word_break(hwp)
            except Exception as e:
                log(f"  ⚠ 자간조정 중단: {e}")
        if apply_indent_align:
            log("[STAGE 2] (3/3) 들여쓰기 재정렬 — 자간 drift 보정")
            try:
                align_left_indent(hwp)
            except Exception as e:
                log(f"  ⚠ 2차 들여쓰기 정렬 중단: {e}")

    # ─── 모드별 저장 ────────────────────────────────
    if mode == "new":
        if out_path:
            log(f"[STAGE 1] hwpx 저장: {out_path}")
            save_as_hwpx(hwp, out_path)
            log("[STAGE 1] ✔ 완료")
            return out_path
        else:
            log("[STAGE 1] ✔ 변환 완료 — 저장은 caller 책임 (지연-저장)")
            return ""
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
    initial_prev_emitted: bool = False,
) -> None:
    """
    노드 리스트 순회 — 타입에 따라 적절한 렌더러 호출.

    ★ Blank 처리 정책 (2026-04-30 갱신):
      모든 비빈 노드 사이에 정확히 1줄 8pt 빈 단락을 보장 (consistent prepend).
        - md 소스에 빈 줄 없어도 자동으로 1줄 삽입 → 모든 변형 사이 일관 spacing
        - md 소스에 빈 줄 1+개 있어도 1줄로 coalesce (중복 제거)
        - 첫 노드 앞 / 마지막 노드 뒤에는 자동 삽입 안 함
      이 dispatcher 가 prepend 를 단일 책임으로 처리하므로, section/subsection/
      conclusion/note_callout/attachment 의 내부 "위 빈 줄" 코드는 제거됨.

    initial_prev_emitted:
      True 면 dispatcher 호출 시점에 이미 비빈 콘텐츠가 emit 된 상태로 가정 →
      첫 노드 앞에 자동으로 1줄 prepend. "new" 모드에서 메타데이터 헤더 직후
      본문 시작 시 사용 (헤더와 본문 사이 spacing 보장).
    """
    def _emit_blank_para() -> None:
        try:
            p.set_font_size(hwp, spec.blank_para_pt)
            p.break_para(hwp)
        except Exception:
            pass

    last_was_emit = bool(initial_prev_emitted)  # 직전이 비빈 노드 emit 였는가
    for node in nodes:
        if node.type == "blank":
            # 소스 명시 빈 줄 — 직전이 비빈 노드일 때만 emit (선두/연속 빈줄 무시)
            if last_was_emit:
                _emit_blank_para()
                last_was_emit = False
            continue
        # 비빈 노드 — 직전도 비빈 노드였으면 자동으로 1줄 prepend
        if last_was_emit:
            _emit_blank_para()
        try:
            _dispatch_one(hwp, node, spec)
            last_was_emit = True
        except Exception as e:
            log(f"  ✘ 노드 렌더링 실패 ({node.type} marker={node.marker!r}): {e}")
            try:
                p.break_para(hwp)
            except Exception:
                pass
            last_was_emit = True  # 실패해도 break_para 했으므로 prepend 위치 갱신


def _dispatch_one(hwp: Any, node: Node, spec: ReportSpec) -> None:
    """한 노드 → 적절한 렌더러 1회 호출. blank 노드는 _dispatch_nodes 가 처리."""

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
            # 마커 없는 본문 — Sentinel 이 [붙임] 다음 줄글 prose 를 마커 없이
            # 채우는 케이스 등. spec.annotation (hotkey S = 맑은 고딕 12pt SSOT)
            # 폰트/크기 적용 — 직전 단락의 폰트가 그대로 흐르는 사고 방지.
            # indent 는 직전 글머리 indent 가 잔류해 들여쓰기 안 풀리는 사고
            # 회피 위해 0 으로 reset. fixed_pre/post / 마커 글리프는 없음.
            _emit_unmarkered_prose(hwp, spec, node.text)
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

    # 알 수 없는 타입 — 마커 없는 본문과 동일 처리 (annotation 폰트로)
    if node.text:
        _emit_unmarkered_prose(hwp, spec, node.text)


def _emit_unmarkered_prose(hwp: Any, spec: ReportSpec, text: str) -> None:
    """마커 없는 줄글 단락 — annotation spec(hotkey S, var_font2 SSOT) 으로 emit.

    [붙임] 다음 prose, 또는 알 수 없는 타입의 fallback 경로에서 호출. 직전
    단락의 글머리 폰트/들여쓰기가 그대로 새는 사고를 막기 위해 명시적으로
    annotation spec 으로 reset.
    """
    a = spec.annotation
    p.set_font(hwp, a.font, a.size_pt, bold=False)
    p.set_line_spacing(hwp, a.line_spacing)
    p.set_indent(hwp, 0.0)
    p.align(hwp, "justify")
    p.insert_text(hwp, text)
    p.break_para(hwp)


# ============================================================================
# 저장
# ============================================================================

def save_as_hwpx(hwp: Any, out_path: str) -> None:
    """SaveAs .hwpx — Windows 경로 형식, format='HWPX' 우선 시도.

    public 함수 — generate_hwpx_via_com 내부 호출 + 변환 후 지연-저장 시
    markdown_tab 같은 caller 가 직접 호출 (out_path=None 으로 변환 끝낸 뒤).
    """
    out_path = out_path.replace("/", "\\")
    try:
        hwp.SaveAs(out_path, "HWPX", "")
    except TypeError:
        hwp.SaveAs(out_path)


# ============================================================================
# 활성 문서 — 선택 영역 텍스트를 마크다운으로 해석하여 그 자리에 변환 출력
# ============================================================================

def convert_selection_to_hwpx(
    hwp: Any,
    spec: ReportSpec = REPORT1_SPEC,
    log: callable = print,
) -> int:
    """
    한/글 활성 문서의 선택 영역을 plain text 로 추출 → md 파싱 →
    선택 영역을 변환 결과로 대체 치환.

    동작 순서:
      1. selection_range() 로 영역 검사. 단순 캐럿이면 NoSelectionError raise.
      2. GetTextFile("TEXT", "saveblock") 로 선택 영역의 텍스트만 추출.
         서식은 모두 버림 — 사용자 의도가 'plain md 로 보겠다' 이므로.
      3. Run("Delete") 로 선택 영역 제거 — 캐럿이 그 자리에 남음.
      4. parse_markdown(text) → generate_hwpx_via_com(mode='cursor') 로
         그 위치에 변환 결과를 emit.

    배경:
      Tk Text 위젯의 한글 IME 가 매끄럽지 않아 '내장 에디터에 직접 타이핑'
      UX 가 어색하다. 한/글 자체의 IME 는 매우 잘 동작하므로 사용자가
      한/글에서 md 본문을 타이핑한 뒤 영역 선택 → Ctrl+Shift+X 로 즉석 변환
      하는 동선이 더 자연스럽다 (마크다운 탭은 외부 작성자 산출 md 의 일괄
      변환 전용으로 좁힘).

      한/글에서 손쉽게 입력하기 위해 글머리 alias `ㅁ`(U+3141) → `□`,
      `ㅇ`(U+3147) → `○` 가 parser 에 이미 반영되어 있고, front-matter 도
      필요 없음 (메타데이터는 새 파일 변환에서만 의미가 있고 cursor 모드는
      스킵).

      ★ 권위 reference — tool2 (금감원 오피스 프로그램) `마크다운()` 메서드
      (한컴라이브러리_decompiled.py L12102~) 가 정확히 동일한 패턴:
      `블록텍스트()` 로 selection 추출 → `HAction.Run('Delete')` → marker
      별 emit. 우리는 GetTextFile 한 번 호출로 전체 받아 외부 파서에 위임.

    Returns:
        삽입된 본문 노드 개수 (디버깅·로그용).

    Raises:
        NoSelectionError: 선택 영역이 없거나 비어있는 경우.
    """
    # ── selection 진단 ──
    # selection_range 는 GetSelectedPosBySet 기반 (모든 한/글 버전 호환).
    # 그래도 안전망으로 GetTextFile 결과 길이도 보조 판정에 사용한다.
    sel = selection_range(hwp)
    log(f"[md-convert] selection_range = {sel}")

    # 1) 선택 영역 텍스트 추출 — 서식 버리고 plain text 만.
    #    GetTextFile(format, option): 한컴 공식 (HwpAutomation_2504.txt p.24).
    #      format="TEXT"   — 일반 텍스트. 유니코드 전용 정보(한자·고어 등) 손실.
    #                        한국어 prose 는 무손실. ※ HWPML2X 는 서식 포함이라 X.
    #      option="saveblock" — 선택 블록만 export (콜론·:true 같은 suffix 없음).
    #    한계: 개체 선택 상태(표·이미지·도형) 에서는 동작 안 함 — 텍스트
    #          selection 만 처리.
    try:
        raw = hwp.GetTextFile("TEXT", "saveblock")
    except Exception as e:
        log(f"[md-convert] GetTextFile 호출 실패: {e}")
        raise NoSelectionError(
            "선택 영역 텍스트 추출 실패 — 한/글 selection 상태를 확인해 주세요."
        ) from e

    text = (raw or "").rstrip()
    log(f"[md-convert] GetTextFile 결과 = {len(text)}자")

    # 2) 판정 (보수적 — 비안전한 경우 거부)
    #    text 가 비어있으면: 선택 없음 OR 개체 선택 (GetTextFile 미동작) → 거부.
    #    text 가 있고 sel 도 valid 이면: 정상.
    #    text 가 있는데 sel 가 None 이면: GetSelectedPos 가 거짓음성 (일부 한/글
    #       버전·DRM·포커스 상태에서 발생) — 진행하되 warning 로그.
    if not text:
        raise NoSelectionError(
            "선택 영역이 인식되지 않거나 텍스트가 비어있습니다.\n"
            "• 한/글에서 변환할 본문을 마우스 드래그로 영역 지정 후 단축키를 눌러주세요.\n"
            "• 단순 캐럿 위치만으로는 동작하지 않습니다.\n"
            "• 표·이미지 등 개체 선택 상태에서도 동작하지 않습니다."
        )
    if sel is None:
        log("[md-convert] ⚠ GetSelectedPos 거짓음성 — GetTextFile 결과로 진행")
    log(f"[md-convert] 추출 텍스트 {len(text)}자, {text.count(chr(10)) + 1}줄")

    # 2) 영역 삭제 — 캐럿이 그 자리에 남음. 이어서 cursor 모드로 emit.
    #    Run("Delete") (action id=102, flag=none): tool2 `마크다운()` 메서드도
    #    동일하게 `HAction.Run('Delete')` 사용 — 표준 키보드 Delete 키 동작.
    log("[md-convert] 선택 영역 삭제 (Run='Delete')")
    hwp.Run("Delete")

    # 3) 파싱 + cursor 모드 변환
    doc = parse_markdown(text)
    log(f"[md-convert] parse: 본문 노드 {len(doc.nodes)}개")
    generate_hwpx_via_com(
        hwp, doc, out_path="",
        spec=spec, log=log, mode="cursor",
        작성부서=None, 작성일=None,
        is_new_session=False,
    )
    log(f"[md-convert] ✔ 변환 완료 ({len(doc.nodes)} 노드)")
    return len(doc.nodes)
