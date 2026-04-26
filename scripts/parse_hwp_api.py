"""HWP API 4종 PDF(추출된 .txt)를 파싱하여 hwp_api_db에 적재.

입력: reference/official_pdfs/*.txt (PyMuPDF로 사전 추출됨)
출력: MariaDB hwp_api_db 5개 테이블

실행 전 scripts/hwp_api_schema.sql로 스키마 생성 필요.
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path
from typing import Iterator

import pymysql

BASE = Path(__file__).resolve().parent.parent / "reference" / "official_pdfs"
ACTION_TXT = BASE / "ActionTable_2504.txt"
PSET_TXT = BASE / "ParameterSetTable_2504.txt"
AUTOMATION_TXT = BASE / "HwpAutomation_2504.txt"
EVENT_TXT = BASE / "한글오토메이션EventHandler추가_2504.txt"

DB_CFG = dict(
    host=os.environ.get("MYSQL_HOST", "localhost"),
    port=int(os.environ.get("MYSQL_PORT", 3306)),
    user=os.environ.get("MYSQL_USER", "root"),
    password=os.environ.get("MYSQL_PASSWORD", "genius"),
    database="hwp_api_db",
    charset="utf8mb4",
    autocommit=False,
)

KOR_RE = re.compile(r"[가-힣]")
PAGE_HEADER_RE = re.compile(r"^=+ Page (\d+) =+\s*$")
PSET_ID_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_]*\*?$")
MEMBER_HEADER_RE = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\((Method|Property|Event)\)\s*$")
SECTION_RE = re.compile(r"^(Description|Declaration|Parameters?|Return|Remark|Example)\s*$")
PSET_SECTION_RE = re.compile(r"^\d+\)\s+([A-Za-z][A-Za-z0-9_]*)\s*[:：]\s*(.*)$")


def has_korean(s: str) -> bool:
    return bool(KOR_RE.search(s))


def iter_pages(txt: str) -> Iterator[tuple[int, list[str]]]:
    """===== Page N ===== 헤더로 분할된 페이지를 (page_num, lines) 형태로 yield."""
    page_num = 0
    buf: list[str] = []
    for line in txt.splitlines():
        m = PAGE_HEADER_RE.match(line)
        if m:
            if buf:
                yield page_num, buf
            page_num = int(m.group(1))
            buf = []
        else:
            buf.append(line)
    if buf:
        yield page_num, buf


def clean_lines(lines: list[str]) -> list[str]:
    """페이지 머리의 페이지번호 라인 등 노이즈 제거."""
    out = []
    for ln in lines:
        s = ln.strip()
        if not s:
            continue
        # 페이지 번호만 있는 라인 (1~3자리 숫자)
        if re.fullmatch(r"\d{1,3}", s):
            continue
        out.append(s)
    return out


# ============================================================
# 1. ActionTable
# ============================================================
ACTION_TABLE_HEADERS = {"Action ID", "ParameterSet ID", "Description", "비고"}
ACTION_TABLE_PREAMBLE_END = "BookMark"  # 본문 시작 추정 키 — 안전하게 첫 페이지 헤더만 스킵


def parse_action_table(txt: str) -> list[dict]:
    """ActionTable .txt → [{action_id, parameterset_id, parameterset_flag, description, note, page_number}]"""
    rows: list[dict] = []
    in_table = False  # 헤더 4행 통과 여부

    # 모든 페이지를 합쳐서 한 흐름으로 처리 (헤더가 매 페이지 반복됨)
    flat: list[tuple[int, str]] = []
    for page_num, lines in iter_pages(txt):
        for ln in clean_lines(lines):
            flat.append((page_num, ln))

    # 헤더(4행)를 만나면 다음 4행을 스킵하고 본문 처리
    i = 0
    n = len(flat)

    def is_pset_token(s: str) -> bool:
        return s in ("-", "+") or bool(PSET_ID_RE.fullmatch(s))

    def is_action_id_candidate(s: str) -> bool:
        # ASCII 시작, 한국어 없음, 헤더 단어 아님
        if not s or has_korean(s):
            return False
        if s in ACTION_TABLE_HEADERS:
            return False
        # 영문/숫자/공백/물결/언더바로만 구성
        return bool(re.fullmatch(r"[A-Za-z][A-Za-z0-9_ ~/]*\d*", s))

    def is_continuation(s: str) -> bool:
        # 한국어 포함 / 괄호 / 숫자 시작 / 소문자 시작 / 특수기호 시작
        if has_korean(s):
            return True
        if s.startswith(("(", "[", "*", "-", "0x", "bit ", "·", "※")):
            return True
        if s and s[0].islower():
            return True
        return False

    # 첫 페이지의 도입부(범례) 무시 — Action ID 첫 등장까지 스킵
    while i < n:
        s = flat[i][1]
        if s in ACTION_TABLE_HEADERS:
            # 헤더 그룹 시작. 연속된 헤더 단어들 모두 스킵
            while i < n and flat[i][1] in ACTION_TABLE_HEADERS:
                i += 1
            in_table = True
            break
        i += 1

    while i < n:
        page_num, s = flat[i]

        # 페이지 헤더 다시 등장하면 스킵
        if s in ACTION_TABLE_HEADERS:
            while i < n and flat[i][1] in ACTION_TABLE_HEADERS:
                i += 1
            continue

        if not is_action_id_candidate(s):
            i += 1
            continue

        # 행 시작
        action_id = s
        i += 1
        pset_id = None
        pset_flag = "plain"

        # ParameterSet ID 후보 (다음 줄이 단일 토큰인 경우)
        if i < n:
            nxt = flat[i][1]
            if nxt == "-":
                pset_flag = "none"
                pset_id = None
                i += 1
            elif nxt == "+":
                pset_flag = "pending"
                pset_id = None
                i += 1
            elif PSET_ID_RE.fullmatch(nxt) and not has_korean(nxt):
                # 진짜 ParameterSet ID인지 한 번 더 검증:
                # 다음 줄이 한국어(Description) 또는 다음 Action ID로 이어져야 함
                if nxt.endswith("*"):
                    pset_flag = "required"
                    pset_id = nxt[:-1]
                else:
                    pset_flag = "plain"
                    pset_id = nxt
                i += 1
            # else: ParameterSet 칸이 비어있는 행

        # Description 누적 (다음 Action ID 후보 직전까지)
        desc_lines: list[str] = []
        while i < n:
            page_num2, t = flat[i]
            if t in ACTION_TABLE_HEADERS:
                break
            if is_continuation(t):
                desc_lines.append(t)
                i += 1
                continue
            if is_action_id_candidate(t):
                # 다음 줄이 ParameterSet 토큰이거나 한국어면 → 새 행 시작
                if i + 1 < n:
                    nx = flat[i + 1][1]
                    if is_pset_token(nx) or has_korean(nx) or nx in ACTION_TABLE_HEADERS:
                        break
                    # 그 다음 줄도 모호하면 일단 새 행으로 본다
                    break
                else:
                    break
            # 모호한 라인은 description에 흡수
            desc_lines.append(t)
            i += 1

        rows.append({
            "action_id": action_id,
            "parameterset_id": pset_id,
            "parameterset_flag": pset_flag,
            "description": " ".join(desc_lines).strip() or None,
            "note": None,
            "page_number": page_num,
        })

    return rows


# ============================================================
# 2. ParameterSetTable
# ============================================================
def parse_parameterset_table(txt: str) -> list[dict]:
    """ParameterSetTable .txt → [{set_id, description, section_index, page_number, items: [...]}]"""
    sets: list[dict] = []
    current: dict | None = None
    in_table = False
    has_subtype = False
    cur_row: dict | None = None
    cur_field = "item_id"  # item_id → type → (sub_type) → description
    item_ord = 0

    def flush_row():
        nonlocal cur_row, item_ord
        if cur_row and cur_row.get("item_id"):
            cur_row["ord"] = item_ord
            current["items"].append(cur_row)
            item_ord += 1
        cur_row = None

    def flush_set():
        nonlocal current
        if current is not None:
            flush_row()
            sets.append(current)
            current = None

    for page_num, lines in iter_pages(txt):
        for ln in clean_lines(lines):
            # 섹션 헤더: "1) ActionCrossRef : 상호참조 삽입"
            m = PSET_SECTION_RE.match(ln)
            if m:
                flush_set()
                current = {
                    "set_id": m.group(1),
                    "description": m.group(2).strip() or None,
                    "section_index": int(re.match(r"^(\d+)", ln).group(1)),
                    "page_number": page_num,
                    "items": [],
                }
                in_table = False
                has_subtype = False
                cur_row = None
                cur_field = "item_id"
                item_ord = 0
                continue

            if current is None:
                continue

            # 표 헤더 감지 (Item ID / Type / [SubType] / Description)
            if ln == "Item ID":
                # 다음 줄들이 헤더 4-3개일 가능성 — 단순화: 헤더 시작
                in_table = "expect_type"
                cur_row = None
                cur_field = "item_id"
                continue
            if in_table == "expect_type" and ln == "Type":
                in_table = "expect_sub_or_desc"
                continue
            if in_table == "expect_sub_or_desc" and ln == "SubType":
                has_subtype = True
                in_table = "expect_desc"
                continue
            if in_table == "expect_sub_or_desc" and ln == "Description":
                has_subtype = False
                in_table = True
                continue
            if in_table == "expect_desc" and ln == "Description":
                in_table = True
                continue

            if not in_table:
                # 섹션 시작 직후 표가 아닌 보충 설명일 수 있음 → description에 보강
                if current["description"]:
                    current["description"] += " " + ln
                else:
                    current["description"] = ln
                continue

            # 본문 행 파싱
            # cur_field 상태 머신: item_id → type → (sub_type) → description (멀티라인)
            if cur_row is None:
                cur_row = {"item_id": None, "item_type": None, "sub_type": None, "description": ""}
                cur_field = "item_id"

            if cur_field == "item_id":
                cur_row["item_id"] = ln
                cur_field = "type"
                continue

            if cur_field == "type":
                cur_row["item_type"] = ln
                cur_field = "sub_type" if has_subtype else "description"
                continue

            if cur_field == "sub_type":
                # SubType이 있는 표라도 SubType 칸이 비어있을 수 있음
                # 휴리스틱: SubType은 PIT_*이거나 짧은 ASCII. 한국어면 description 시작.
                if has_korean(ln) or len(ln) > 30:
                    cur_row["sub_type"] = None
                    cur_row["description"] = ln
                    cur_field = "description"
                else:
                    cur_row["sub_type"] = ln
                    cur_field = "description"
                continue

            if cur_field == "description":
                # 다음 행 시작 감지: PIT_* 직전의 라인이 다음 item_id
                # 휴리스틱: 영문 식별자(한국어 없음) + 다음 줄이 PIT_*면 새 행
                # 또는 한국어 없는 짧은 라인 + 다음 줄도 영문이면 새 행
                # 단순화: 라인이 한국어 없고, 식별자형이며 길이 < 50, 다음 라인이 PIT_*나 식별자면 새 행
                is_id_like = (
                    not has_korean(ln)
                    and len(ln) < 50
                    and bool(re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", ln))
                )
                # lookahead 없이 단순 판정: ID 같으면 새 행
                if is_id_like:
                    flush_row()
                    cur_row = {"item_id": ln, "item_type": None, "sub_type": None, "description": ""}
                    cur_field = "type"
                else:
                    if cur_row["description"]:
                        cur_row["description"] += " " + ln
                    else:
                        cur_row["description"] = ln
                continue

    flush_set()

    # description 정리
    for s in sets:
        for it in s["items"]:
            it["description"] = (it["description"] or "").strip() or None

    return sets


# ============================================================
# 3. HwpAutomation + EventHandler
# ============================================================
def parse_members(txt: str, source_file: str, default_kind: str | None = None) -> list[dict]:
    """HwpAutomation .txt → [{name, kind, description, declaration, ..., raw_text, source_file, page_number, items}]"""
    members: list[dict] = []
    flat: list[tuple[int, str]] = []
    for page_num, lines in iter_pages(txt):
        for ln in clean_lines(lines):
            flat.append((page_num, ln))

    # 멤버 헤더 위치 찾기
    headers: list[tuple[int, int, str, str]] = []  # (idx, page_num, name, kind)
    for idx, (pg, ln) in enumerate(flat):
        m = MEMBER_HEADER_RE.match(ln)
        if m:
            headers.append((idx, pg, m.group(1), m.group(2)))

    if not headers:
        # EventHandler처럼 헤더 패턴이 다를 수 있음. default_kind로 단일 멤버 처리.
        if default_kind:
            members.append({
                "name": Path(source_file).stem,
                "kind": default_kind,
                "description": "\n".join(ln for _, ln in flat),
                "declaration": None, "parameters_text": None, "return_text": None, "remark": None,
                "raw_text": "\n".join(ln for _, ln in flat),
                "source_file": source_file,
                "page_number": flat[0][0] if flat else None,
                "items": [],
            })
        return members

    # 각 헤더 사이의 본문을 멤버로
    for h_i, (idx, pg, name, kind) in enumerate(headers):
        end = headers[h_i + 1][0] if h_i + 1 < len(headers) else len(flat)
        body = flat[idx + 1: end]
        body_lines = [ln for _, ln in body]
        raw = "\n".join(body_lines)

        sections = split_sections(body_lines)
        items = extract_inline_items(body_lines)

        members.append({
            "name": name,
            "kind": kind,
            "description": sections.get("Description"),
            "declaration": sections.get("Declaration"),
            "parameters_text": sections.get("Parameters"),
            "return_text": sections.get("Return"),
            "remark": sections.get("Remark"),
            "raw_text": raw,
            "source_file": source_file,
            "page_number": pg,
            "items": items,
        })

    return members


def split_sections(lines: list[str]) -> dict[str, str]:
    """Description / Declaration / Parameters / Return / Remark 분리."""
    sections: dict[str, list[str]] = {}
    cur = None
    for ln in lines:
        m = SECTION_RE.match(ln)
        if m:
            cur = m.group(1)
            if cur == "Parameter":
                cur = "Parameters"
            sections.setdefault(cur, [])
            continue
        if cur:
            # 내부 표 헤더 라인은 스킵
            if ln in ("Item ID", "Type", "Description") and cur in ("Remark", "Parameters"):
                # 내부 표 시작 — 이 섹션의 텍스트는 표 직전까지만
                cur = None
                continue
            sections[cur].append(ln)
    return {k: "\n".join(v).strip() or None for k, v in sections.items()}


def extract_inline_items(lines: list[str]) -> list[dict]:
    """본문 안에 'Item ID / Type / Description' 표가 있으면 추출."""
    items = []
    i = 0
    n = len(lines)
    while i < n:
        # 표 헤더 검출
        if (
            i + 2 < n
            and lines[i] == "Item ID"
            and lines[i + 1] == "Type"
            and lines[i + 2] == "Description"
        ):
            i += 3
            ord_idx = 0
            cur = None
            cur_field = "item_id"
            while i < n:
                ln = lines[i]
                # 표 종료 시그널: 새 섹션 마커 또는 다음 멤버 헤더(이미 분리됨이므로 발생X)
                if SECTION_RE.match(ln):
                    break
                if cur_field == "item_id":
                    cur = {"item_id": ln, "item_type": None, "description": "", "ord": ord_idx}
                    cur_field = "type"
                elif cur_field == "type":
                    cur["item_type"] = ln
                    cur_field = "description"
                elif cur_field == "description":
                    is_id_like = (
                        not has_korean(ln)
                        and len(ln) < 50
                        and bool(re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", ln))
                    )
                    if is_id_like:
                        cur["description"] = cur["description"].strip() or None
                        items.append(cur)
                        ord_idx += 1
                        cur = {"item_id": ln, "item_type": None, "description": "", "ord": ord_idx}
                        cur_field = "type"
                    else:
                        cur["description"] += (" " if cur["description"] else "") + ln
                i += 1
            if cur and cur.get("item_id"):
                cur["description"] = (cur["description"] or "").strip() or None
                items.append(cur)
            continue
        i += 1
    return items


# ============================================================
# Loader
# ============================================================
def load(actions, parametersets, members):
    conn = pymysql.connect(**DB_CFG)
    try:
        with conn.cursor() as cur:
            cur.execute("DELETE FROM hwp_member_items")
            cur.execute("DELETE FROM hwp_members")
            cur.execute("DELETE FROM hwp_actions")
            cur.execute("DELETE FROM hwp_parameterset_items")
            cur.execute("DELETE FROM hwp_parametersets")

            cur.executemany(
                """INSERT INTO hwp_actions
                   (action_id, parameterset_id, parameterset_flag, description, note, page_number)
                   VALUES (%(action_id)s, %(parameterset_id)s, %(parameterset_flag)s,
                           %(description)s, %(note)s, %(page_number)s)""",
                actions,
            )

            for ps in parametersets:
                cur.execute(
                    """INSERT INTO hwp_parametersets (set_id, description, section_index, page_number)
                       VALUES (%s, %s, %s, %s)""",
                    (ps["set_id"], ps["description"], ps["section_index"], ps["page_number"]),
                )
                ps_id = cur.lastrowid
                if ps["items"]:
                    cur.executemany(
                        """INSERT INTO hwp_parameterset_items
                           (parameterset_id, item_id, item_type, sub_type, description, ord)
                           VALUES (%s, %s, %s, %s, %s, %s)""",
                        [
                            (ps_id, it["item_id"], it["item_type"], it["sub_type"], it["description"], it["ord"])
                            for it in ps["items"]
                        ],
                    )

            for m in members:
                cur.execute(
                    """INSERT INTO hwp_members
                       (name, kind, description, declaration, parameters_text,
                        return_text, remark, raw_text, source_file, page_number)
                       VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
                    (m["name"], m["kind"], m["description"], m["declaration"],
                     m["parameters_text"], m["return_text"], m["remark"],
                     m["raw_text"], m["source_file"], m["page_number"]),
                )
                m_id = cur.lastrowid
                if m["items"]:
                    cur.executemany(
                        """INSERT INTO hwp_member_items
                           (member_id, item_id, item_type, description, ord)
                           VALUES (%s, %s, %s, %s, %s)""",
                        [(m_id, it["item_id"], it["item_type"], it["description"], it["ord"])
                         for it in m["items"]],
                    )

        conn.commit()
    finally:
        conn.close()


def main() -> int:
    print("[1/4] ActionTable 파싱...")
    actions = parse_action_table(ACTION_TXT.read_text(encoding="utf-8"))
    print(f"  → actions: {len(actions)}건")

    print("[2/4] ParameterSetTable 파싱...")
    parametersets = parse_parameterset_table(PSET_TXT.read_text(encoding="utf-8"))
    items_total = sum(len(p["items"]) for p in parametersets)
    print(f"  → parametersets: {len(parametersets)}건, items: {items_total}건")

    print("[3/4] HwpAutomation 파싱...")
    members = parse_members(AUTOMATION_TXT.read_text(encoding="utf-8"), AUTOMATION_TXT.name)
    member_items_total = sum(len(m["items"]) for m in members)
    print(f"  → members: {len(members)}건, inline items: {member_items_total}건")

    print("[4/4] EventHandler 파싱...")
    events = parse_members(EVENT_TXT.read_text(encoding="utf-8"), EVENT_TXT.name, default_kind="Event")
    print(f"  → events: {len(events)}건")
    members.extend(events)

    print("\nMariaDB 적재 중...")
    load(actions, parametersets, members)
    print("완료.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
