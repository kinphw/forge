"""
Forge 사용자 개인 설정 영속화 — `%APPDATA%\\Forge\\settings.json`.

Windows 표준 위치(`%APPDATA% = C:\\Users\\<user>\\AppData\\Roaming`) 에 저장.
다중 사용자 PC 에서 사용자별 분리가 자동, 프로젝트 폴더가 깨끗하게 유지됨.
trade-off: portable exe 를 다른 PC 로 옮길 때 설정은 따라오지 않음 — 의도된 선택.

저장 항목 (현재):
  - `keymap: {action_id: key_letter or null}` — 사용자가 변경한 hotkey 만 저장.
    `null` = 명시적 비활성화, key 누락 = [ACTIONS][forge.ui.actions.ACTIONS] 의
    `default_key` fallback.

향후 확장 후보:
  - fonts/sizes (var_font1~4, var_size1~4, var_blank_size)
  - 마지막 선택한 한/글 인스턴스 moniker

설계:
  - 단일 파일 단일 JSON. 항목별 read/write 헬퍼 (`get_keymap`, `set_keymap_entry`)
    가 매 호출마다 load → mutate → save. 작은 파일이라 성능 무관, 동시성 단순.
  - write 실패 (권한 부족 등) 는 `False` 반환. 호출자가 알릴지 무시할지 결정.
  - 손상된 JSON 은 빈 dict 로 fallback — 사용자 작업 흐름 안 끊김.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Optional


def settings_dir() -> Path:
    """`%APPDATA%\\Forge\\` (또는 fallback `~/.forge/`). 디렉토리는 lazy 생성."""
    appdata = os.environ.get("APPDATA")
    if appdata:
        return Path(appdata) / "Forge"
    # APPDATA 없는 비정상 Windows / 비-Windows fallback
    return Path.home() / ".forge"


def settings_path() -> Path:
    """settings.json 의 전체 경로."""
    return settings_dir() / "settings.json"


def load() -> dict:
    """settings.json 로드. 파일 없거나 손상 시 빈 dict 반환 (silent)."""
    p = settings_path()
    if not p.exists():
        return {}
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def save(data: dict) -> bool:
    """settings.json 저장. write 실패 시 False (권한·디스크 풀 등)."""
    p = settings_path()
    try:
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(
            json.dumps(data, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        return True
    except OSError:
        return False


# ── keymap helpers ────────────────────────────────────────────────────────

def get_keymap() -> dict[str, Optional[str]]:
    """저장된 keymap 만 반환. `{action_id: key_letter or None}`.
    키 누락 = default 사용, value=None = 비활성화."""
    raw = load().get("keymap") or {}
    # 값 검증: 1글자 문자열 또는 None 만 허용
    out: dict[str, Optional[str]] = {}
    for k, v in raw.items():
        if v is None:
            out[k] = None
        elif isinstance(v, str) and len(v) == 1 and v.isalpha():
            out[k] = v.upper()
    return out


def set_keymap_entry(action_id: str, key: Optional[str]) -> bool:
    """keymap 한 항목 수정 후 즉시 flush. key=None 이면 비활성화 저장."""
    data = load()
    keymap = data.setdefault("keymap", {})
    if key is None:
        keymap[action_id] = None
    else:
        keymap[action_id] = key.upper()
    return save(data)
