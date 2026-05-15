"""
탭 ② 마크다운 입력 — md 에디터 + 변환 버튼.

워크플로 (지연-저장 패턴, 2026-05-08):
  1. 좌측 텍스트 영역에 개조식 md 입력 (또는 파일 불러오기)
  2. "▶ 새 hwpx 파일로 변환" 클릭 → 즉시 한/글 새 문서 생성 + 본문 emit
  3. 변환 끝나면 결과를 화면으로 본 뒤 저장 dialog 표시 → 파일명 지정 → SaveAs
  4. 저장 dialog 취소 시 한/글에 결과 문서만 떠있는 상태로 마무리 (강제 저장 X)

★ 2026-04-28: 본 탭은 외부 작성자가 만든 md 일괄 변환 전용.
'활성 문서 커서에 삽입' 옵션은 탭 ① 의 'Ctrl+Shift+X — 선택 영역 → md 변환'
으로 대체되어 제거 (한/글 IME 가 직접 매끄럽게 처리하므로 사용자가 한/글에서
타이핑한 뒤 영역 선택 → 단축키 호출이 더 자연스럽다).
"""
from __future__ import annotations

import threading
import tkinter as tk
from datetime import datetime
from tkinter import filedialog, messagebox, ttk
from tkinter.constants import LEFT, RIGHT, BOTH, X, Y, W, E
from tkinter.ttk import LabelFrame as TtkLabelFrame, PanedWindow as TtkPanedWindow
from typing import TYPE_CHECKING, Optional

from forge import user_settings
from forge.formatter import generate_hwpx_via_com, parse_markdown, save_as_hwpx
from forge.hwp_session import (
    MultipleHwpInstancesError,
    NoExistingHwpError,
    init_com_for_thread,
)

if TYPE_CHECKING:
    from ..app import AppState, ForgeApp


SAMPLE_MD = """\
---
보고서명: ◆◆◆ 검사 결과 및 대응방안
---

1. 현황

가. 개요
□ (배경) __은행권 광고 동의 위반__ 사례 적발 — 금감원 정기검사 결과
 ○ __A사__·__B사__가 마케팅 이용·광고수신 동의 미보유 고객의 __개인신용정보__ 활용
  - A사: 2024.1~6 약 1.2만 건
  - B사: 2024.3~9 약 0.8만 건
   · 동의 유효기간 경과 케이스 다수 포함

나. 검사 진행
□ (현황) 자료 회신 __90% 수령 완료__
 ○ 잔여 자료 추가 요구 발송 (5.7.) — 회신 기한 5.15.

* 검사 표본은 신용정보법 제33조의2 위반 의심 건 위주 추출
※ 본 수치는 잠정치 — 최종 결과는 검사 종료 시점에 확정
† 위반 건수 = 동의 미보유 + 유효기간 경과 합계 기준

[참고]
신용정보법 제33조의2(개인신용정보의 이용) 제2항
신용정보주체로부터 마케팅 이용·광고수신 동의를 받지 아니하고는 광고를 위하여 개인신용정보를 이용할 수 없다.

2. 향후 계획

가. 즉시 조치
□ (제재) __위반사항 확정__ 시 과태료 부과 검토
□ (개선) __동의 관리 체계 점검__ 및 시스템 차원 개선 권고

나. 중장기
□ (제도) 광고 동의 갱신 주기 가이드라인 마련
 ○ 업계 의견 수렴 (5월 중) → 감독규정 개정 검토 (6~7월)

=> 검사 결과를 토대로 __업계 차원의 시스템적 개선__ 유도

[붙임 1]
관련 법령 발췌
신용정보법 제33조의2 — 개인신용정보 이용 동의 의무
신용정보법 시행령 제27조의2 제3항 — 광고 목적 이용 제한

[붙임 2]
검사 대상 회사별 위반 건수 잠정치
A사: 12,345 건 (2024.1~6 누적)
B사:  8,567 건 (2024.3~9 누적)
합계 약 2.0만 건 — 추가 자료 회신 후 확정.
"""


class MarkdownTab:
    def __init__(self, parent: tk.Misc, app: "ForgeApp"):
        self.app = app
        self.state = app.state
        self.frame = ttk.Frame(parent, padding=12)

        # ─── 상단 툴바 ───
        toolbar = ttk.Frame(self.frame)
        toolbar.pack(fill=X, pady=(0, 8))

        ttk.Button(toolbar, text="🧪 샘플 채우기", command=self._fill_sample
                    ).pack(side=LEFT, padx=(0, 4))
        ttk.Button(toolbar, text="🧹 비우기", command=self._clear
                    ).pack(side=LEFT, padx=(0, 4))

        ttk.Separator(toolbar, orient="vertical").pack(side=LEFT, fill=Y, padx=8)

        self.btn_convert = ttk.Button(
            toolbar, text="▶ 새 hwpx 파일로 변환",
            command=self._on_convert,
        )
        self.btn_convert.pack(side=LEFT)
        # 항상 활성 — 클릭 시 한/글 자동 attach (lazy).
        # 활성 문서 커서에 삽입은 탭 ① 의 Ctrl+Shift+X 로 이관.

        # ─── 메타데이터 입력 (spec v1.4: 작성부서·작성일은 markdown 이 아닌 UI 영역) ───
        meta_bar = ttk.Frame(self.frame)
        meta_bar.pack(fill=X, pady=(0, 8))

        # 영속화된 값 (user_settings.markdown section) → var 초기값. 누락 시
        # code default fallback. var_date 만 의도적 제외 — 일자는 매번 다르므로
        # 영속화하면 어제 날짜가 그대로 박히는 사고.
        md_settings = user_settings.get_section("markdown")

        ttk.Label(meta_bar, text="작성부서:", width=10).pack(side=LEFT)
        self.var_dept = tk.StringVar(value=str(md_settings.get("dept", "")))
        ttk.Entry(meta_bar, textvariable=self.var_dept, width=30).pack(side=LEFT, padx=(0, 12))

        ttk.Label(meta_bar, text="작성일:", width=8).pack(side=LEFT)
        self.var_date = tk.StringVar(value=datetime.today().strftime("%Y-%m-%d"))
        ttk.Entry(meta_bar, textvariable=self.var_date, width=14).pack(side=LEFT, padx=(0, 8))
        ttk.Label(meta_bar, text="(YYYY-MM-DD, 비우면 변환 시 오늘)").pack(side=LEFT)

        # ─── 양식 spec — 페이지 여백 + 줄간격 (구 ② 기본정보 탭 통합, 2026-05-08) ───
        # 폰트·글머리·주석 spec 은 '개별 작업' 탭의 4 폰트 cluster + 빈줄 크기가
        # SSOT — 변환 시점에 자동 주입. 여기는 구조 양식(여백·줄간격) 만 다룬다.
        spec_bar = TtkLabelFrame(self.frame, text="양식 spec — 페이지 여백·줄간격", padding=8)
        spec_bar.pack(fill=X, pady=(0, 8))

        m = self.state.spec.margins
        def _mg(key: str, default: float) -> float:
            try:
                return float(md_settings.get(f"margin_{key}", default))
            except (TypeError, ValueError):
                return default
        self.var_margins = {
            "left":   tk.DoubleVar(value=_mg("left",   m.left)),
            "right":  tk.DoubleVar(value=_mg("right",  m.right)),
            "top":    tk.DoubleVar(value=_mg("top",    m.top)),
            "bottom": tk.DoubleVar(value=_mg("bottom", m.bottom)),
            "header": tk.DoubleVar(value=_mg("header", m.header)),
            "footer": tk.DoubleVar(value=_mg("footer", m.footer)),
        }
        margin_labels = [("좌", "left"), ("우", "right"), ("위", "top"),
                          ("아래", "bottom"), ("머리", "header"), ("꼬리", "footer")]
        for label, key in margin_labels:
            ttk.Label(spec_bar, text=label, foreground="#666").pack(side=LEFT, padx=(0, 2))
            ttk.Spinbox(
                spec_bar, from_=0, to=50, increment=0.5,
                textvariable=self.var_margins[key], width=5,
            ).pack(side=LEFT, padx=(0, 6))
        ttk.Label(spec_bar, text="mm", foreground="#999").pack(side=LEFT, padx=(0, 12))

        ttk.Separator(spec_bar, orient="vertical").pack(side=LEFT, fill=Y, padx=(0, 8))

        ttk.Label(spec_bar, text="줄간격").pack(side=LEFT, padx=(0, 4))
        _ln_default = self.state.spec.line_spacing_default
        try:
            _ln_init = int(md_settings.get("line_default", _ln_default))
        except (TypeError, ValueError):
            _ln_init = _ln_default
        self.var_line_default = tk.IntVar(value=_ln_init)
        ttk.Spinbox(
            spec_bar, from_=100, to=300, increment=5,
            textvariable=self.var_line_default, width=5,
        ).pack(side=LEFT, padx=(0, 2))
        ttk.Label(spec_bar, text="%", foreground="#999").pack(side=LEFT, padx=(0, 12))

        ttk.Button(
            spec_bar, text="↺ 기본값", command=self._reset_spec,
        ).pack(side=LEFT, padx=(0, 4))
        ttk.Button(
            spec_bar, text="설정 적용", command=self._apply_spec,
        ).pack(side=LEFT)

        # ─── 좌·우 분할 ───
        paned = TtkPanedWindow(self.frame, orient="horizontal")
        paned.pack(fill=BOTH, expand=True)

        # 좌: md 에디터
        left = TtkLabelFrame(paned, text="개조식 markdown 입력", padding=6)
        paned.add(left, weight=3)

        self.text = tk.Text(left, wrap="none", font=("Consolas", 11), undo=True)
        scroll_y = ttk.Scrollbar(left, orient="vertical", command=self.text.yview)
        scroll_x = ttk.Scrollbar(left, orient="horizontal", command=self.text.xview)
        self.text.configure(yscrollcommand=scroll_y.set, xscrollcommand=scroll_x.set)
        self.text.grid(row=0, column=0, sticky="nsew")
        scroll_y.grid(row=0, column=1, sticky="ns")
        scroll_x.grid(row=1, column=0, sticky="ew")
        left.rowconfigure(0, weight=1)
        left.columnconfigure(0, weight=1)

        # 우: 진행 로그
        right = TtkLabelFrame(paned, text="진행 로그 / 결과", padding=6)
        paned.add(right, weight=2)

        self.log = tk.Text(right, wrap="word", font=("Consolas", 10),
                           state="disabled", height=20)
        log_scroll = ttk.Scrollbar(right, orient="vertical", command=self.log.yview)
        self.log.configure(yscrollcommand=log_scroll.set)
        self.log.pack(side=LEFT, fill=BOTH, expand=True)
        log_scroll.pack(side=RIGHT, fill=Y)

        # 영속화: 변경 감지 (trace) 와 영속화된 값을 spec 에 silent 동기화.
        # 사용자가 "설정 적용" 안 눌러도 영속화된 margin/줄간격이 다음 변환에 반영.
        self._wire_markdown_persistence()
        self._apply_vars_to_spec(silent=True)

    # 한/글 연결은 lazy — 변환 클릭 시 app.ensure_hwp() 호출.
    # 따로 on_hwp_ready 콜백 불필요.

    # ─────────────────────────────────────── 툴바 핸들러
    def _fill_sample(self) -> None:
        """샘플 markdown 채우기 + 작성부서를 '테스트팀' 으로 prefill."""
        self.text.delete("1.0", "end")
        self.text.insert("1.0", SAMPLE_MD)
        self.var_dept.set("테스트팀")
        self._log("샘플 markdown 채움 (작성부서='테스트팀')")

    def _clear(self) -> None:
        self.text.delete("1.0", "end")

    # ─────────────────────────────────────── 변환 핸들러
    def _on_convert(self) -> None:
        """
        변환 → 저장 흐름 (지연-저장 패턴, 2026-05-08 갱신):

          1. 클릭 즉시 변환 시작 (파일명 사전 입력 X)
          2. 한/글에 새 문서 생성 + 본문 emit + STAGE 2 후처리
          3. 변환 끝나면 사용자에게 결과 화면을 보여준 상태로 저장 dialog 호출
          4. 저장 dialog 에서 파일명 지정 → SaveAs (또는 취소 시 미저장 — 사용자가
             한/글 메뉴에서 직접 저장 가능)

        근거: 이전엔 클릭 즉시 파일명을 묻고 변환하던 방식이라, 결과를
        보지도 않고 파일명을 정해야 했음. 변환 후 결과를 본 뒤 이름을 정하는
        흐름이 자연스러움.
        """
        # 한/글 연결은 worker 안에서 lazy 로 처리 (ensure_hwp).
        src = self.text.get("1.0", "end").rstrip()
        if not src:
            messagebox.showinfo("입력 없음", "markdown 을 입력하거나 '샘플 채우기' 를 눌러보세요.")
            return

        # UI 블로킹 방지: 백그라운드 스레드에서 변환만 진행. 저장은 변환 후 별도 단계.
        self.btn_convert.configure(state="disabled", text="변환 중...")
        threading.Thread(
            target=self._convert_worker,
            args=(src,), daemon=True,
        ).start()

    def _convert_worker(self, src: str) -> None:
        # 백그라운드 스레드에서 COM 사용 — CoInitialize 필수
        # (이거 없으면 'CoInitialize가 호출되지 않았습니다' 오류)
        init_com_for_thread()
        try:
            self._log("─" * 50)
            self._log("[mode] 새 hwpx 변환 (지연-저장)")
            self._log("[parse] markdown 파싱 시작")
            doc = parse_markdown(src)
            self._log(f"[parse] 보고서명={doc.metadata.보고서명!r} (front-matter)")

            # spec v1.4: 작성부서·작성일은 UI 입력에서
            부서 = (self.var_dept.get() or "").strip() or None
            일자 = (self.var_date.get() or "").strip() or datetime.today().strftime("%Y-%m-%d")
            self._log(f"[ui] 작성부서={부서!r} 작성일={일자!r} (UI 입력)")
            self._log(f"[parse] 본문 노드 {len(doc.nodes)}개")

            # 한/글 lazy attach
            self._log("[hwp] 한/글 연결 확인 (필요 시 attach)")
            session = self.app.ensure_hwp()
            hwp = session.hwp

            # SSOT 주입 — realtime_tab 의 4 폰트 cluster + var_blank_size 를
            # state.spec 에 mutate. state.spec 자체를 in-place 갱신하므로
            # settings_tab 의 다른 필드(margins 등) 사용자 입력은 그대로 유지.
            try:
                rt = getattr(self.app, "tab_realtime", None)
                if rt is not None and hasattr(rt, "apply_overrides_to_spec"):
                    rt.apply_overrides_to_spec(self.state.spec)
                    self._log(
                        f"[ssot] realtime_tab 폰트 적용 — "
                        f"본문={self.state.spec.bullets[0].font}/{self.state.spec.bullets[0].size_pt}pt, "
                        f"주석={self.state.spec.annotation.font}/{self.state.spec.annotation.size_pt}pt, "
                        f"헤드라인={self.state.spec.title_font}, "
                        f"요약={self.state.spec.bullet_summary_font}, "
                        f"빈줄={self.state.spec.blank_para_pt}pt"
                    )
            except Exception as e:
                self._log(f"[ssot] override skip ({type(e).__name__}: {e}) — spec 기본값 사용")

            # 변환 — 렌더러 dispatcher (out_path=None 으로 저장 단계 skip)
            generate_hwpx_via_com(
                hwp, doc, out_path=None,
                spec=self.state.spec, log=self._log, mode="new",
                작성부서=부서, 작성일=일자,
                is_new_session=session.is_new,
            )
            self._log("─" * 50)
            self._log("✔ 변환 완료 — 저장 위치 선택 대기")
            self.app._set_status("✔ 변환 완료 — 저장 위치 선택 대기")

            # ─ 변환 후 저장 dialog (UI 스레드) ─
            # filedialog 는 UI 스레드에서만 안전하게 호출 가능. after(0, ...) 로
            # UI 스레드에 dispatch 한 뒤 사용자 응답 결과를 worker 에 넘기지 않고,
            # UI 스레드 자체에서 저장 worker 를 새로 spawn 한다 (COM 재attach OK —
            # ensure_hwp 가 살아있는 세션 재사용).
            self.frame.after(0, self._prompt_save_after_convert)
        except MultipleHwpInstancesError as e:
            self._log(f"⚠ 한/글 인스턴스 {len(e.instances)}개 — picker 표시")
            self.app.prompt_pick_from_worker(e.instances)
            self.frame.after(0, self._reset_convert_button)
        except NoExistingHwpError as e:
            self._log(f"✘ {e}")
            self.frame.after(0, lambda: messagebox.showwarning(
                "한/글 미실행",
                "떠 있는 한/글 인스턴스가 없습니다.\n\n"
                "한/글을 직접 실행하신 후 (빈 새 문서 또는 임의의 hwpx 파일)\n"
                "다시 변환 버튼을 눌러주세요.\n\n"
                "이미 한/글이 떠 있는데 이 메시지가 나온다면 상단 '한/글 선택' "
                "버튼으로 인스턴스를 골라주세요.",
            ))
            self.frame.after(0, self._reset_convert_button)
        except Exception as e:
            self._log(f"✘ 오류: {e}")
            import traceback
            self._log(traceback.format_exc())
            self.frame.after(0, lambda err=e: messagebox.showerror(
                "변환 실패", f"{type(err).__name__}: {err}"))
            self.frame.after(0, self._reset_convert_button)

    def _prompt_save_after_convert(self) -> None:
        """변환 종료 후 UI 스레드에서 저장 dialog 표시 → 저장 worker spawn.

        사용자가 dialog 를 취소하면 미저장 상태로 마무리. 한/글에 변환 결과
        문서는 그대로 떠 있어 사용자가 한/글 메뉴 [파일 → 다른 이름으로 저장]
        으로 직접 저장 가능 — Forge 가 강제로 저장을 종용하지 않음.
        """
        try:
            out_path = filedialog.asksaveasfilename(
                parent=self.frame.winfo_toplevel(),
                title="hwpx 저장 위치",
                defaultextension=".hwpx",
                filetypes=[("HWPX", "*.hwpx")],
                initialfile=f"forge_draft_{datetime.now().strftime('%Y%m%d_%H%M%S')}.hwpx",
            )
        except Exception as e:
            self._log(f"[save] dialog 호출 실패: {e}")
            self._reset_convert_button()
            return

        if not out_path:
            self._log("[save] 사용자가 저장 취소 — 한/글 화면에 결과 문서만 남음")
            self.app._set_status("⚠ 저장 취소 — 한/글에서 직접 저장하세요")
            self._reset_convert_button()
            return

        # 저장은 별도 worker (COM 재attach 필요)
        self.btn_convert.configure(text="저장 중...")
        threading.Thread(
            target=self._save_worker, args=(out_path,), daemon=True,
        ).start()

    def _save_worker(self, out_path: str) -> None:
        """변환된 한/글 활성 문서를 사용자가 지정한 경로로 SaveAs."""
        init_com_for_thread()
        try:
            session = self.app.ensure_hwp()
            self._log(f"[save] hwpx 저장: {out_path}")
            save_as_hwpx(session.hwp, out_path)
            self._log(f"✔ 저장 완료: {out_path}")
            self.app._set_status(f"✔ 저장 완료 — {out_path}")
        except Exception as e:
            self._log(f"✘ 저장 실패: {e}")
            import traceback
            self._log(traceback.format_exc())
            self.frame.after(0, lambda err=e: messagebox.showerror(
                "저장 실패",
                f"{type(err).__name__}: {err}\n\n"
                f"한/글 화면에 변환 결과 문서가 그대로 남아있으니, "
                f"한/글 메뉴에서 직접 저장을 시도하실 수 있습니다."))
        finally:
            self.frame.after(0, self._reset_convert_button)

    def _reset_convert_button(self) -> None:
        self.btn_convert.configure(state="normal", text="▶ 새 hwpx 파일로 변환")

    # spec v1.4: 메타데이터 fallback 메서드 제거.
    #   - 보고서명: markdown front-matter (Metadata) 에서 직접
    #   - 작성부서·작성일: UI 입력 (var_dept, var_date) — _convert_worker 에서 직접 읽음

    # ─────────────────────────────────────── 양식 spec (구 ② 기본정보 탭)
    def _apply_vars_to_spec(self, silent: bool = False) -> bool:
        """var_margins/var_line_default → state.spec 반영.

        Args:
            silent: True 면 ValueError/TypeError swallow + False 반환.
                    False 면 messagebox 로 사용자에게 안내 + False 반환.
        Returns:
            True 면 mutate 성공.

        __init__ 끝의 silent=True 호출은 영속화된 var 값을 spec 에 동기화하기
        위함. "설정 적용" 버튼은 silent=False 로 명시적 컨펌 다이얼로그 노출.
        """
        from forge.formatter.templates import PageMargins
        try:
            self.state.spec.margins = PageMargins(
                left=float(self.var_margins["left"].get()),
                right=float(self.var_margins["right"].get()),
                top=float(self.var_margins["top"].get()),
                bottom=float(self.var_margins["bottom"].get()),
                header=float(self.var_margins["header"].get()),
                footer=float(self.var_margins["footer"].get()),
            )
            self.state.spec.line_spacing_default = int(self.var_line_default.get())
            return True
        except (ValueError, TypeError) as e:
            if not silent:
                messagebox.showerror("입력 오류", f"숫자 형식 오류: {e}")
            return False

    def _apply_spec(self) -> None:
        """'설정 적용' 버튼 핸들러 — 명시적 적용 + 확인 다이얼로그."""
        if self._apply_vars_to_spec(silent=False):
            messagebox.showinfo("적용됨", "양식 spec 이 다음 변환부터 적용됩니다.")

    # ─────────────────────────────────────── 영속화 (var_date 제외)
    def _wire_markdown_persistence(self) -> None:
        """var_dept/margins/line_default 변경을 user_settings 의 'markdown'
        section 에 500 ms 디바운스로 자동 저장. var_date 만 의도적 제외."""
        self._md_save_pending: dict[str, object] = {}
        self._md_save_after_id: Optional[str] = None
        pairs: list[tuple[str, tk.Variable]] = [
            ("dept",          self.var_dept),
            ("margin_left",   self.var_margins["left"]),
            ("margin_right",  self.var_margins["right"]),
            ("margin_top",    self.var_margins["top"]),
            ("margin_bottom", self.var_margins["bottom"]),
            ("margin_header", self.var_margins["header"]),
            ("margin_footer", self.var_margins["footer"]),
            ("line_default",  self.var_line_default),
        ]
        for key, var in pairs:
            var.trace_add(
                "write",
                lambda *_, k=key, v=var: self._schedule_md_save(k, v.get()),
            )

    def _schedule_md_save(self, key: str, value: object) -> None:
        self._md_save_pending[key] = value
        if self._md_save_after_id is not None:
            try:
                self.app.root.after_cancel(self._md_save_after_id)
            except Exception:
                pass
        self._md_save_after_id = self.app.root.after(500, self._flush_md_save)

    def _flush_md_save(self) -> None:
        self._md_save_after_id = None
        if not self._md_save_pending:
            return
        updates = dict(self._md_save_pending)
        self._md_save_pending.clear()
        ok = user_settings.update_section("markdown", updates)
        if not ok:
            self._log(f"[settings] ⚠ markdown section 저장 실패 — {list(updates)}")

    def _reset_spec(self) -> None:
        """기본값(REPORT1_SPEC) 으로 되돌림."""
        from forge.formatter.templates import REPORT1_SPEC
        self.state.spec = REPORT1_SPEC.clone()
        m = self.state.spec.margins
        self.var_margins["left"].set(m.left)
        self.var_margins["right"].set(m.right)
        self.var_margins["top"].set(m.top)
        self.var_margins["bottom"].set(m.bottom)
        self.var_margins["header"].set(m.header)
        self.var_margins["footer"].set(m.footer)
        self.var_line_default.set(self.state.spec.line_spacing_default)

    # ─────────────────────────────────────── 로그 헬퍼
    def _log(self, msg: str) -> None:
        """thread-safe log append."""
        def _append():
            self.log.configure(state="normal")
            self.log.insert("end", msg + "\n")
            self.log.see("end")
            self.log.configure(state="disabled")
        try:
            self.frame.after(0, _append)
        except Exception:
            print(msg)
