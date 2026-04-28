"""
앱 아이콘 — 프로그램으로 생성 (외부 파일 X, 의존성 X).

Tk 의 PhotoImage 는 png/gif 외에도 빈 캔버스를 만들고 `put(color, to=rect)` 로
사각형 단위 색칠이 가능 — 이 기능만으로 깔끔한 "F" 아이콘을 64×64 로 그려서
`root.iconphoto(True, img)` 에 넘김.

호출자 주의:
    PhotoImage 가 GC 되면 아이콘이 사라짐. 반드시 self._icon = make_app_icon(...)
    같이 인스턴스 속성으로 들고 있을 것.

색상 컨셉:
    background = slate-800 (#1f2937) — 어두운 회청색
    foreground = amber-500 (#f59e0b) — Forge "불꽃" 느낌의 따뜻한 주황
"""
from __future__ import annotations

import tkinter as tk


_BG = "#1f2937"   # slate-800
_FG = "#f59e0b"   # amber-500 (forge fire)


def make_app_icon(root: tk.Misc) -> tk.PhotoImage:
    """64×64 PhotoImage 'F' 아이콘 생성 후 반환.

    레이아웃 (64×64 그리드):
      - 배경 전체: slate
      - F 세로 stem:    x=14..24,  y=12..52   (10w × 40h)
      - F 위 가로 bar:  x=14..50,  y=12..20   (36w ×  8h)
      - F 가운데 bar:   x=14..42,  y=28..35   (28w ×  7h)
    """
    img = tk.PhotoImage(master=root, width=64, height=64)
    # 배경
    img.put(_BG, to=(0, 0, 64, 64))
    # F 글리프 — 3 개 사각형 합성
    img.put(_FG, to=(14, 12, 24, 52))   # stem
    img.put(_FG, to=(14, 12, 50, 20))   # top bar
    img.put(_FG, to=(14, 28, 42, 35))   # middle bar
    return img
