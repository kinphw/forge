"""
탭 ② 마크다운 입력 — md 에디터 + 변환 버튼.

워크플로:
  1. 좌측 텍스트 영역에 개조식 md 입력 (또는 파일 불러오기)
  2. 우측에 진행 로그
  3. "hwpx 생성" 버튼 → 한/글 새 문서 생성 → 본문 삽입 → SaveAs hwpx
  4. 결과 파일 경로를 로그·status 에 표시

탭 ① 의 spec 을 spec 인자로 사용. front-matter 가 있으면 우선,
없으면 탭 ① 의 메타데이터 입력값으로 fallback.
"""
from __future__ import annotations

import threading
import tkinter as tk
from datetime import datetime
from tkinter import filedialog, messagebox
from typing import TYPE_CHECKING

import ttkbootstrap as tb
from tkinter.ttk import LabelFrame as TtkLabelFrame, PanedWindow as TtkPanedWindow
from ttkbootstrap.constants import LEFT, RIGHT, BOTH, X, Y, W, E

from forge.stage_1_formatter import generate_hwpx_via_com, parse_markdown
from forge.hwp_session import init_com_for_thread

if TYPE_CHECKING:
    from ..app import AppState, ForgeApp


SAMPLE_MD = """\
---
보고서명: 샘플 보고서 (Forge 테스트)
작성부서: 디지털전환혁신팀
작성일: 2026-04-26
---

1. 현황
가. 개요
□ (배경) Sentinel-Forge GUI 첫 동작 검증
 ○ 탭 ② 마크다운 → hwpx 변환 경로 확인
  - 한/글 COM 인스턴스 attach
  - 본문 노드별 텍스트 삽입
   · 자동 BreakPara

나. 진행 상황
□ (현재) 1차 구현 완료

[참고]
이 hwpx 는 텍스트 + 기본 폰트만 적용된 초안입니다.
박스·색상·테두리 등 시각 디테일은 후속 STAGE 2 Linter 가 처리합니다.

2. 향후 계획
□ (즉시) STAGE 2 XML 룰 카탈로그 작성
□ (장기) STAGE 3 폴리셔 통합

=> 첫 end-to-end 동작 확인 시 미니멀 성공
"""


class MarkdownTab:
    def __init__(self, parent: tb.Window, app: "ForgeApp"):
        self.app = app
        self.state = app.state
        self.frame = tb.Frame(parent, padding=12)

        # ─── 상단 툴바 ───
        toolbar = tb.Frame(self.frame)
        toolbar.pack(fill=X, pady=(0, 8))

        tb.Button(toolbar, text="🧪 샘플 채우기", command=self._fill_sample,
                    bootstyle="info-outline").pack(side=LEFT, padx=(0, 4))
        tb.Button(toolbar, text="🧹 비우기", command=self._clear,
                    bootstyle="warning-outline").pack(side=LEFT, padx=(0, 4))

        tb.Separator(toolbar, orient="vertical").pack(side=LEFT, fill=Y, padx=8)

        # 출력 모드 라디오
        self.var_output_mode = tk.StringVar(value="new")  # "new" | "cursor"
        tb.Radiobutton(
            toolbar, text="새 hwpx 파일에 생성",
            variable=self.var_output_mode, value="new",
            bootstyle="info-toolbutton",
        ).pack(side=LEFT, padx=(0, 4))
        tb.Radiobutton(
            toolbar, text="현재 hwpx 커서에 삽입",
            variable=self.var_output_mode, value="cursor",
            bootstyle="info-toolbutton",
        ).pack(side=LEFT, padx=(0, 8))

        self.btn_convert = tb.Button(
            toolbar, text="▶ hwpx 생성 (변환)",
            command=self._on_convert, bootstyle="primary",
        )
        self.btn_convert.pack(side=LEFT)
        # 항상 활성 — 클릭 시 한/글 자동 attach (lazy)

        # ─── 메타데이터 입력 (spec v1.4: 작성부서·작성일은 markdown 이 아닌 UI 영역) ───
        meta_bar = tb.Frame(self.frame)
        meta_bar.pack(fill=X, pady=(0, 8))

        tb.Label(meta_bar, text="작성부서:", width=10).pack(side=LEFT)
        self.var_dept = tk.StringVar(value="")
        tb.Entry(meta_bar, textvariable=self.var_dept, width=30).pack(side=LEFT, padx=(0, 12))

        tb.Label(meta_bar, text="작성일:", width=8).pack(side=LEFT)
        self.var_date = tk.StringVar(value=datetime.today().strftime("%Y-%m-%d"))
        tb.Entry(meta_bar, textvariable=self.var_date, width=14).pack(side=LEFT, padx=(0, 8))
        tb.Label(meta_bar, text="(YYYY-MM-DD, 비우면 변환 시 오늘)",
                  bootstyle="secondary").pack(side=LEFT)

        # ─── 좌·우 분할 ───
        paned = TtkPanedWindow(self.frame, orient="horizontal")
        paned.pack(fill=BOTH, expand=True)

        # 좌: md 에디터
        left = TtkLabelFrame(paned, text="개조식 markdown 입력", padding=6)
        paned.add(left, weight=3)

        self.text = tk.Text(left, wrap="none", font=("Consolas", 11), undo=True)
        scroll_y = tb.Scrollbar(left, orient="vertical", command=self.text.yview)
        scroll_x = tb.Scrollbar(left, orient="horizontal", command=self.text.xview)
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
        log_scroll = tb.Scrollbar(right, orient="vertical", command=self.log.yview)
        self.log.configure(yscrollcommand=log_scroll.set)
        self.log.pack(side=LEFT, fill=BOTH, expand=True)
        log_scroll.pack(side=RIGHT, fill=Y)

    # 한/글 연결은 lazy — 변환 클릭 시 app.ensure_hwp() 호출.
    # 따로 on_hwp_ready 콜백 불필요.

    # ─────────────────────────────────────── 툴바 핸들러
    def _fill_sample(self) -> None:
        self.text.delete("1.0", "end")
        self.text.insert("1.0", SAMPLE_MD)
        self._log("샘플 markdown 채움")

    def _clear(self) -> None:
        self.text.delete("1.0", "end")

    # ─────────────────────────────────────── 변환 핸들러
    def _on_convert(self) -> None:
        # 한/글 연결은 worker 안에서 lazy 로 처리 (ensure_hwp).
        src = self.text.get("1.0", "end").rstrip()
        if not src:
            messagebox.showinfo("입력 없음", "markdown 을 입력하거나 '샘플 채우기' 를 눌러보세요.")
            return

        mode = self.var_output_mode.get()  # "new" | "cursor"
        out_path = ""

        if mode == "new":
            # 새 파일 저장 위치 선택
            out_path = filedialog.asksaveasfilename(
                title="hwpx 저장 위치",
                defaultextension=".hwpx",
                filetypes=[("HWPX", "*.hwpx")],
                initialfile=f"forge_draft_{datetime.now().strftime('%Y%m%d_%H%M%S')}.hwpx",
            )
            if not out_path:
                return
        # cursor 모드는 경로 불필요 — 활성 문서의 커서 위치에 바로 삽입

        # UI 블로킹 방지: 백그라운드 스레드
        self.btn_convert.configure(state="disabled", text="변환 중...")
        threading.Thread(
            target=self._convert_worker,
            args=(src, out_path, mode), daemon=True,
        ).start()

    def _convert_worker(self, src: str, out_path: str, mode: str) -> None:
        # 백그라운드 스레드에서 COM 사용 — CoInitialize 필수
        # (이거 없으면 'CoInitialize가 호출되지 않았습니다' 오류)
        init_com_for_thread()
        try:
            self._log("─" * 50)
            self._log(f"[mode] {mode} ({'새 hwpx 파일' if mode=='new' else '활성 문서 커서'})")
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

            # cursor 모드: 활성 문서가 있는지 확인
            if mode == "cursor":
                try:
                    if hwp.XHwpDocuments.Count == 0:
                        raise RuntimeError(
                            "현재 한/글에 열린 문서가 없습니다. "
                            "한/글에서 문서를 열거나 '새 hwpx 파일' 모드를 사용하세요."
                        )
                except AttributeError:
                    pass  # COM API 차이 — 무시

            # 변환 — 렌더러 dispatcher
            result = generate_hwpx_via_com(
                hwp, doc, out_path,
                spec=self.state.spec, log=self._log, mode=mode,
                작성부서=부서, 작성일=일자,
                is_new_session=session.is_new,
            )

            self._log("─" * 50)
            if mode == "new":
                self._log(f"✔ 새 hwpx 파일: {result}")
                self.frame.after(0, lambda r=result: messagebox.showinfo(
                    "완료", f"hwpx 저장 완료:\n{r}"))
            else:
                self._log(f"✔ 활성 문서 커서 위치에 삽입 완료")
                self.frame.after(0, lambda: messagebox.showinfo(
                    "완료", "활성 한/글 문서 커서 위치에 삽입 완료.\n"
                              "저장은 한/글에서 직접 하세요."))
        except Exception as e:
            self._log(f"✘ 오류: {e}")
            import traceback
            self._log(traceback.format_exc())
            self.frame.after(0, lambda err=e: messagebox.showerror(
                "변환 실패", f"{type(err).__name__}: {err}"))
        finally:
            self.frame.after(0, lambda: self.btn_convert.configure(
                state="normal", text="▶ hwpx 생성 (변환)"))

    # spec v1.4: 메타데이터 fallback 메서드 제거.
    #   - 보고서명: markdown front-matter (Metadata) 에서 직접
    #   - 작성부서·작성일: UI 입력 (var_dept, var_date) — _convert_worker 에서 직접 읽음

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
