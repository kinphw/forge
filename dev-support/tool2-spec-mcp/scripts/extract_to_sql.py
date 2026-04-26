"""
한컴라이브러리_decompiled.py → MariaDB seed.sql 생성기.

pycdc 출력에 try without except 가 있어 ast.parse 가 실패하는 경우는
fixup_dangling_try() 로 'except: pass' 를 주입한 뒤 ast 로 정확 파싱.

자동 추출:
  - methods (411): 이름·인자·라인·카테고리·FSS 여부·접두사·co_names·used_actions
  - templates (5): 5종 보고서 템플릿 + 진입점
  - template_steps: 템플릿 본문에서 self.X(args) 호출 시퀀스
  - hwp_actions_used: CreateAction("...") / HAction.Run("...") 패턴 추출
  - source_refs: 메서드별 시작·종료 라인
  - bullet_specs: 금감원글머리지정 / 글머리지정 호출의 스칼라 인자

수동 seed (분석노트 §12.5 기반, SQL 끝):
  - markdown_directives

사용:
  python extract_to_sql.py [<decompiled.py>] [<out.sql>]
"""
import ast
import json
import re
import sys
import io
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

DECOMPILED = sys.argv[1] if len(sys.argv) > 1 else \
    r"c:/projects/sentinel-forge/reference/tool2/_unpacked/한컴라이브러리_decompiled.py"
OUT = sys.argv[2] if len(sys.argv) > 2 else \
    r"c:/projects/sentinel-forge/dev-support/tool2-spec-mcp/scripts/seed.sql"

SOURCE_FILE_REL = "reference/tool2/_unpacked/한컴라이브러리_decompiled.py"

FSS_TEMPLATES = {
    "금감보고서":   ("일반",     "금감보고서"),
    "금감원페이지": ("원페이지", "금감원페이지"),
    "금감업무정보": ("업무정보", "금감업무정보"),
    "금감보도자료": ("보도자료", "금감보도자료"),
    "금감원장보고": ("원장",     "금감원장보고"),
}


# ─────────────────────────────────────────────────────────────────────
# 0) Fixup: pycdc 가 만든 dangling try 블록에 'except: pass' 주입
# ─────────────────────────────────────────────────────────────────────
TRY_RE = re.compile(r'^(\s*)try:\s*$')


# `.0` / `.1` 등의 CPython 내부 변수명. 숫자 리터럴 (예: 0.5) 과 구분하기
# 위해 negative lookbehind 로 word 문자가 아닌 위치에서만 매칭.
PYCDC_DOT_VAR_RE = re.compile(r'(?<![\w.])\.(\d+)\b')


PYCDC_FOR_NONE_RE = re.compile(r'\bfor\s+None\s+in\b')


def fixup_pycdc_quirks(src: str) -> str:
    """
    pycdc 가 만드는 Python 문법 위반 패턴 교정.

    1) `lambda .0:` / `for x in .0` 등에서 `.<digit>` 는 CPython 내부의
       comprehension iterable 익명 변수명. Python 소스에서는 사용 불가.
       `_<digit>` 으로 치환.

    2) `for None in range(...):` — pycdc 가 미사용 루프 변수를 None 으로
       표기. `for _ in ...` 로 치환.
    """
    src = PYCDC_DOT_VAR_RE.sub(r'_\1', src)
    src = PYCDC_FOR_NONE_RE.sub('for _ in', src)
    return src


def fixup_dangling_try(src: str) -> str:
    """
    every `try:` 블록을 검사하여 매칭되는 except/finally 가 없으면
    같은 들여쓰기로 'except: pass' 를 주입한다.

    들여쓰기 규칙:
      try: 의 indent = N 이면, 본체는 N+4 이상.
      처음으로 indent <= N 인 비-blank 라인이 except/finally 가 아니라면
      그 직전 위치에 'except: pass' 를 N 들여쓰기로 삽입.
    """
    lines = src.splitlines()
    insertions: list[tuple[int, str]] = []  # (insert_before_line_idx, text)

    for i, line in enumerate(lines):
        m = TRY_RE.match(line)
        if not m:
            continue
        try_indent = len(m.group(1))
        # 다음 line 부터 스캔
        j = i + 1
        end_idx = len(lines)
        found_handler = False
        while j < len(lines):
            nl = lines[j]
            stripped = nl.strip()
            if not stripped:
                j += 1
                continue
            indent = len(nl) - len(nl.lstrip())
            if indent <= try_indent:
                # block 종료. except/finally 인지 확인
                if stripped.startswith(("except", "finally")):
                    found_handler = True
                else:
                    end_idx = j
                break
            j += 1
        else:
            end_idx = len(lines)

        if not found_handler:
            insertions.append((end_idx, " " * try_indent + "except: pass"))

    if not insertions:
        return src

    # 뒤에서부터 삽입 (인덱스 안 깨지게)
    for idx, text in sorted(insertions, key=lambda x: -x[0]):
        lines.insert(idx, text)

    return "\n".join(lines) + "\n"


# ─────────────────────────────────────────────────────────────────────
# 분류 휴리스틱
# ─────────────────────────────────────────────────────────────────────
def categorize(name: str) -> str:
    if name.startswith(("글자", "글머리")):  return "글자"
    if name.startswith(("문단", "줄간격", "내어쓰기")):  return "문단"
    if name.startswith("문서"):  return "문서"
    if name.startswith("표") and not name.startswith("표지"):  return "표"
    if name.startswith("셀"):  return "셀"
    if name.startswith("블록"):  return "블록"
    if name.startswith("쪽"):  return "쪽"
    if name.startswith("마크다운"):  return "마크다운"
    org_prefixes = ("금감", "행안부", "경남", "남해", "서울", "인천", "경기",
                    "용인", "해수", "관세", "제주", "병무", "영덕", "외교",
                    "충북교", "충주", "금천", "광주광역", "서교공")
    if name.startswith(org_prefixes):  return "템플릿"
    return "기타"


def org_prefix(name: str) -> str | None:
    for prefix in ("금감보고서", "금감원페이지", "금감업무정보",
                   "금감보도자료", "금감원장", "금감보도", "금감원",
                   "행안부", "서교공", "광주광역", "충북교",
                   "경남", "남해", "서울", "인천", "경기", "용인",
                   "해수", "관세", "제주", "병무", "영덕", "외교",
                   "충주", "금천"):
        if name.startswith(prefix):
            return prefix
    return None


# ─────────────────────────────────────────────────────────────────────
# AST 헬퍼
# ─────────────────────────────────────────────────────────────────────
def extract_self_calls(node: ast.FunctionDef) -> list[tuple[str, list[ast.expr]]]:
    """본문 출현 순서대로 self.<name>(args) 호출 추출."""
    out = []
    for sub in ast.walk(node):
        if isinstance(sub, ast.Call):
            f = sub.func
            if (isinstance(f, ast.Attribute) and isinstance(f.value, ast.Name)
                    and f.value.id == "self"):
                out.append((f.attr, sub.args))
    # ast.walk 는 순회 순서가 토픽적이라 lineno 기준 정렬
    out.sort(key=lambda x: (
        getattr(x[1][0] if x[1] else None, "lineno", 0),
        getattr(x[1][0] if x[1] else None, "col_offset", 0),
    ) if x[1] else (0, 0))
    return out


def extract_self_calls_ordered(node: ast.FunctionDef) -> list[tuple[str, list[ast.expr]]]:
    """lineno + col_offset 으로 정확한 본문 순서 보장."""
    found = []
    for sub in ast.walk(node):
        if isinstance(sub, ast.Call):
            f = sub.func
            if (isinstance(f, ast.Attribute) and isinstance(f.value, ast.Name)
                    and f.value.id == "self"):
                lineno = getattr(sub, "lineno", 0)
                col = getattr(sub, "col_offset", 0)
                found.append((lineno, col, f.attr, sub.args))
    found.sort()
    return [(name, args) for (_, _, name, args) in found]


def extract_action_uses(node: ast.FunctionDef) -> list[str]:
    """CreateAction("X") / HAction.Run("X") / GetDefault("X") / Execute("X") 패턴."""
    actions = set()
    for sub in ast.walk(node):
        if isinstance(sub, ast.Call) and len(sub.args) >= 1:
            f = sub.func
            if isinstance(f, ast.Attribute) and f.attr in (
                "CreateAction", "Run", "GetDefault", "Execute"
            ):
                arg0 = sub.args[0]
                if isinstance(arg0, ast.Constant) and isinstance(arg0.value, str):
                    actions.add(arg0.value)
    return sorted(actions)


def extract_self_attrs(node: ast.FunctionDef) -> list[str]:
    out = set()
    for sub in ast.walk(node):
        if isinstance(sub, ast.Attribute) and isinstance(sub.value, ast.Name) \
                and sub.value.id == "self":
            out.add(sub.attr)
    return sorted(out)


def repr_args(args: list[ast.expr]) -> str:
    out = []
    for a in args:
        try:
            out.append(ast.unparse(a))
        except Exception:
            out.append("?")
    return ", ".join(out)


def parse_scalar(node: ast.expr):
    """ast.expr → Python 값 (가능한 경우)."""
    if isinstance(node, ast.Constant):
        return node.value
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.USub) \
            and isinstance(node.operand, ast.Constant):
        return -node.operand.value
    return None


def parse_margins_call(args: list[ast.expr]) -> dict | None:
    """문서여백(L, R, T, B, H, F) → dict."""
    if len(args) != 6:
        return None
    vals = [parse_scalar(a) for a in args]
    if any(v is None for v in vals):
        return None
    return {"L": vals[0], "R": vals[1], "T": vals[2],
            "B": vals[3], "H": vals[4], "F": vals[5]}


# ─────────────────────────────────────────────────────────────────────
# 1) 파일 로드 + fixup + ast.parse
# ─────────────────────────────────────────────────────────────────────
src_raw = Path(DECOMPILED).read_text(encoding="utf-8")
src_a = fixup_pycdc_quirks(src_raw)
src_fixed = fixup_dangling_try(src_a)
diff_lines = src_fixed.count("\n") - src_raw.count("\n")
print(f"# fixup injected {diff_lines} 'except: pass' lines", file=sys.stderr)

# 그래도 남아있는 pycdc quirk 들은 SyntaxError 가 가리키는 라인을
# 주석화하고 재시도 (whack-a-mole 자동화).
def parse_with_retry(src: str, max_iter: int = 200) -> tuple[ast.AST, int]:
    """SyntaxError 발생 라인을 commenting 하고 재시도. 성공한 tree + 폐기 줄수 반환."""
    lines = src.splitlines()
    commented = 0
    for _ in range(max_iter):
        try:
            return ast.parse("\n".join(lines)), commented
        except SyntaxError as e:
            ln = (e.lineno or 1) - 1
            if 0 <= ln < len(lines):
                # 이미 주석 처리된 줄이면 더 위로 올라가며 commenting (블록 전체)
                if lines[ln].lstrip().startswith("#"):
                    # 한 줄 더 위 / 또는 이미 commented면 그 다음 줄
                    ln_orig = ln
                    while ln < len(lines) and lines[ln].lstrip().startswith("#"):
                        ln += 1
                    if ln >= len(lines):
                        ln = ln_orig
                lines[ln] = "# [pycdc-fixup] " + lines[ln]
                commented += 1
            else:
                raise
    raise RuntimeError(f"parse_with_retry exceeded {max_iter} iterations")


tree, n_commented = parse_with_retry(src_fixed)
print(f"# parse_with_retry commented out {n_commented} bad lines", file=sys.stderr)
cls_node = next(
    (n for n in ast.walk(tree) if isinstance(n, ast.ClassDef) and n.name == "기본한컴"),
    None
)
if cls_node is None:
    print("ERROR: class 기본한컴 not found", file=sys.stderr)
    sys.exit(1)

methods = [n for n in cls_node.body if isinstance(n, ast.FunctionDef)]
print(f"# parsed {len(methods)} methods", file=sys.stderr)


# ─────────────────────────────────────────────────────────────────────
# 2) 메서드별 종료 라인 추정 (decompiled 원본 기준 — fixup 전 lineno 와
#    fixup 후 lineno 는 차이날 수 있으나, 메서드 본문이 추가되지는
#    않았으므로 시작 라인은 일치한다고 보고 다음 메서드 시작 -1 로 추정)
# ─────────────────────────────────────────────────────────────────────
method_line_end: dict[str, int] = {}
for i, m in enumerate(methods):
    if i + 1 < len(methods):
        method_line_end[m.name] = methods[i + 1].lineno - 1
    else:
        method_line_end[m.name] = cls_node.end_lineno or m.lineno + 50


# ─────────────────────────────────────────────────────────────────────
# 3) SQL 출력
# ─────────────────────────────────────────────────────────────────────
def sql_str(s) -> str:
    if s is None:
        return "NULL"
    if not isinstance(s, str):
        s = str(s)
    return "'" + s.replace("\\", "\\\\").replace("'", "''") + "'"


def sql_json(obj) -> str:
    if obj is None:
        return "NULL"
    return "'" + json.dumps(obj, ensure_ascii=False).replace("\\", "\\\\").replace("'", "''") + "'"


def sql_num(n) -> str:
    return "NULL" if n is None else str(n)


lines: list[str] = []
lines += [
    "-- =====================================================================",
    "-- tool2-spec-mcp seed data",
    "-- 생성: extract_to_sql.py (ast 기반, fixup_dangling_try 적용)",
    f"-- 원본: {SOURCE_FILE_REL}",
    f"-- 메서드 수: {len(methods)}",
    "-- =====================================================================",
    "",
    "SET NAMES utf8mb4;",
    "",
    "-- 기존 데이터 삭제 (idempotent)",
    "DELETE FROM source_refs;",
    "DELETE FROM hwp_actions_used;",
    "DELETE FROM bullet_specs;",
    "DELETE FROM template_steps;",
    "DELETE FROM templates;",
    "DELETE FROM markdown_directives;",
    "DELETE FROM methods;",
    "ALTER TABLE methods             AUTO_INCREMENT = 1;",
    "ALTER TABLE templates           AUTO_INCREMENT = 1;",
    "ALTER TABLE template_steps      AUTO_INCREMENT = 1;",
    "ALTER TABLE bullet_specs        AUTO_INCREMENT = 1;",
    "ALTER TABLE markdown_directives AUTO_INCREMENT = 1;",
    "ALTER TABLE hwp_actions_used    AUTO_INCREMENT = 1;",
    "ALTER TABLE source_refs         AUTO_INCREMENT = 1;",
    "",
    "-- ───────── methods (411) ─────────",
]

method_id_by_name: dict[str, int] = {}
method_actions: dict[str, list[str]] = {}

for i, m in enumerate(methods, start=1):
    method_id_by_name[m.name] = i
    arg_names = [a.arg for a in m.args.args]
    method_actions[m.name] = extract_action_uses(m)
    co_names = extract_self_attrs(m)
    cat = categorize(m.name)
    fss = 1 if m.name.startswith("금감") else 0
    op = org_prefix(m.name)
    lines.append(
        f"INSERT INTO methods (name, args_json, arg_count, category, fss_specific, "
        f"org_prefix, decompiled_line, brief, co_names_json, used_actions) VALUES "
        f"({sql_str(m.name)}, {sql_json(arg_names)}, {len(arg_names)}, "
        f"{sql_str(cat)}, {fss}, {sql_str(op)}, {m.lineno}, NULL, "
        f"{sql_json(co_names)}, {sql_json(method_actions[m.name])});"
    )

lines += ["", "-- ───────── source_refs ─────────"]
for m in methods:
    mid = method_id_by_name[m.name]
    lines.append(
        f"INSERT INTO source_refs (method_id, source_kind, file_path, line_start, line_end) VALUES "
        f"({mid}, 'decompiled', {sql_str(SOURCE_FILE_REL)}, {m.lineno}, {method_line_end[m.name]});"
    )

lines += ["", "-- ───────── hwp_actions_used ─────────"]
n_actions_total = 0
for m in methods:
    mid = method_id_by_name[m.name]
    for action in method_actions[m.name]:
        n_actions_total += 1
        lines.append(
            f"INSERT INTO hwp_actions_used (action_name, method_id, items_json) VALUES "
            f"({sql_str(action)}, {mid}, NULL);"
        )

# templates
lines += ["", "-- ───────── templates ─────────"]
template_id_by_name: dict[str, int] = {}
tmpl_idx = 0
for m in methods:
    if m.name not in FSS_TEMPLATES:
        continue
    tmpl_idx += 1
    template_id_by_name[m.name] = tmpl_idx
    cat, entry = FSS_TEMPLATES[m.name]
    template_args = [a.arg for a in m.args.args if a.arg != "self"]

    margins = None
    primary_font = None
    for cname, cargs in extract_self_calls_ordered(m):
        if cname == "문서여백" and margins is None:
            margins = parse_margins_call(cargs)
        if cname == "폰트" and primary_font is None and len(cargs) >= 1:
            v = parse_scalar(cargs[0])
            if isinstance(v, str):
                primary_font = v

    mvals = (
        sql_num(margins["L"] if margins else None),
        sql_num(margins["R"] if margins else None),
        sql_num(margins["T"] if margins else None),
        sql_num(margins["B"] if margins else None),
        sql_num(margins["H"] if margins else None),
        sql_num(margins["F"] if margins else None),
    )
    lines.append(
        f"INSERT INTO templates (name, category, entry_method, args_json, "
        f"margin_l_mm, margin_r_mm, margin_t_mm, margin_b_mm, margin_h_mm, margin_f_mm, "
        f"line_spacing, primary_font, title_font, decompiled_line, notes) VALUES "
        f"({sql_str(m.name)}, {sql_str(cat)}, {sql_str(entry)}, {sql_json(template_args)}, "
        f"{mvals[0]}, {mvals[1]}, {mvals[2]}, {mvals[3]}, {mvals[4]}, {mvals[5]}, "
        f"NULL, {sql_str(primary_font)}, NULL, {m.lineno}, NULL);"
    )

# template_steps
lines += ["", "-- ───────── template_steps ─────────"]
n_steps = 0
for m in methods:
    if m.name not in template_id_by_name:
        continue
    tid = template_id_by_name[m.name]
    for order, (cname, cargs) in enumerate(extract_self_calls_ordered(m), start=1):
        n_steps += 1
        args_repr = repr_args(cargs)
        if len(args_repr) > 250:
            args_repr = args_repr[:250] + "..."
        lines.append(
            f"INSERT INTO template_steps (template_id, step_order, method_name, "
            f"args_repr, purpose) VALUES "
            f"({tid}, {order}, {sql_str(cname)}, {sql_str(args_repr)}, NULL);"
        )

# bullet_specs (금감원글머리지정 / 글머리지정 호출에서 자동 추출)
lines += ["", "-- ───────── bullet_specs (글머리지정 호출 자동 추출) ─────────"]
MD_GLYPH_MAP = {"□": "□", "◦": "○", "○": "○", "●": "○",
                "-": "-", "·": "·", "†": "·", "✦": "·"}
n_bullets = 0
for tname, tid in template_id_by_name.items():
    tmethod = next((m for m in methods if m.name == tname), None)
    if tmethod is None:
        continue
    level = 0
    for cname, cargs in extract_self_calls_ordered(tmethod):
        if cname not in ("금감원글머리지정", "글머리지정"):
            continue
        vals = [parse_scalar(a) for a in cargs]
        if cname == "금감원글머리지정" and len(vals) >= 10:
            glyph, font, size, indent, bold, sa, ls, fp, fpost, leadin = vals[:10]
        elif cname == "글머리지정" and len(vals) >= 7:
            glyph, font, size, indent, bold, sa, ls = vals[:7]
            fp, fpost, leadin = 0, 0, None
        else:
            continue
        level += 1
        glyph_s = glyph if isinstance(glyph, str) else "?"
        md_glyph = MD_GLYPH_MAP.get(glyph_s, glyph_s)
        n_bullets += 1
        lines.append(
            f"INSERT INTO bullet_specs (template_id, level, md_glyph, out_glyph, "
            f"font, size_pt, indent_pt, bold, space_above_pt, line_spacing, "
            f"fixed_pre, fixed_post, leadin_size_pt, notes) VALUES "
            f"({tid}, {level}, {sql_str(md_glyph)}, {sql_str(glyph_s)}, "
            f"{sql_str(font if isinstance(font, str) else None)}, "
            f"{sql_num(size if isinstance(size, (int, float)) else None)}, "
            f"{sql_num(indent if isinstance(indent, (int, float)) else None)}, "
            f"{1 if bold == 1 else 0}, "
            f"{sql_num(sa if isinstance(sa, (int, float)) else None)}, "
            f"{sql_num(int(ls) if isinstance(ls, (int, float)) else None)}, "
            f"{fp if isinstance(fp, int) else 0}, "
            f"{fpost if isinstance(fpost, int) else 0}, "
            f"{sql_num(leadin if isinstance(leadin, (int, float)) else None)}, NULL);"
        )

# markdown_directives (수동 seed)
lines += ["", "-- ───────── markdown_directives (수동 seed, 분석노트 §12.5) ─────────"]
DIRECTIVES = [
    ("네모",     ["사각형"], "□ ",     "Bold + 맑은 고딕", "bullet",  0, "큰 항목 글머리",                            "□"),
    ("사각형",   ["네모"],   "□ ",     "Bold + 맑은 고딕", "bullet",  0, "큰 항목 글머리 (네모와 동일)",              "□"),
    ("동그라미", ["원"],     " ○ ",    "휴먼명조",         "bullet",  0, "중간 항목 글머리",                          "○"),
    ("원",       ["동그라미"]," ○ ",   "휴먼명조",         "bullet",  0, "중간 항목 글머리 (동그라미와 동일)",        "○"),
    ("바",       [],        "   - ",   "휴먼명조",         "bullet",  0, "작은 항목 글머리",                          "-"),
    ("당구",     ["당구장"], "    ※ ", "휴먼명조",         "bullet",  0, "주의/일반 주석 (※)",                        "※"),
    ("당구장",   ["당구"],   "    ※ ", "휴먼명조",         "bullet",  0, "주의/일반 주석 (당구와 동일)",              "※"),
    ("주석",     ["주석1"],  "     * ", "휴먼명조",        "bullet",  0, "본문 참조 주석",                            "*"),
    ("주석1",    ["주석"],   "     * ", "휴먼명조",        "bullet",  0, "본문 참조 주석 (주석과 동일)",              "*"),
    ("주석2",    [],         "    ** ", "휴먼명조",        "bullet",  0, "이중 참조 주석 (Forge 미정의)",             "**"),
    ("참고",     [],         None,      "박스 (어두운 헤더)", "box",   0, "참고 박스 callout (제목 박스)",             None),
    ("상단박스", [],         None,      "박스 (긴 본문)",    "box",   0, "단락 박스 callout",                         None),
    ("소제목",       [],     None, "Roman/Number 자동 카운팅", "heading", 1, "[1] [2] [3] ... 자동 번호",            None),
    ("숫자소제목",   [],     None, "Roman/Number 자동 카운팅", "heading", 1, "소제목과 동일 카운터",                  None),
    ("소제목숫자",   [],     None, "Roman/Number 자동 카운팅", "heading", 1, "내용 길이에 따라 폭 자동",              None),
    ("소제목로마",   [],     None, "Roman 자동 카운팅",         "heading", 1, "[I] [II] [III] ... 자동",                None),
    ("로마소제목",   [],     None, "Roman 자동 카운팅",         "heading", 1, "소제목로마와 동일",                      None),
    ("표",       [],         None, "표 자동 생성",       "table",   0,
        "연속 '표:' 라인 누적 → 표 1개. 첫 줄=헤더, 빈 줄로 표 분리, "
        "첫 셀 숫자=데이터행, 칸 수 초과는 무시", None),
    ("엔터",     [],         "\\n",    "BreakPara",        "special", 0, "'엔터:엔터' = 빈 줄 삽입",                   None),
    ("#오늘날짜",[],         None,     "datetime.today",   "special", 0, "YYYYMMDD 치환",                              None),
]
for kw, aliases, tok, style, cat, ac, desc, eq in DIRECTIVES:
    lines.append(
        f"INSERT INTO markdown_directives (keyword, aliases_json, output_token, "
        f"output_style, category, auto_count, description, forge_md_equiv) VALUES "
        f"({sql_str(kw)}, {sql_json(aliases) if aliases else 'NULL'}, "
        f"{sql_str(tok)}, {sql_str(style)}, {sql_str(cat)}, {ac}, "
        f"{sql_str(desc)}, {sql_str(eq)});"
    )

lines += ["", "-- 끝."]

out_text = "\n".join(lines) + "\n"
Path(OUT).write_text(out_text, encoding="utf-8")
print(f"# wrote {OUT}", file=sys.stderr)
print(f"# stats: methods={len(methods)} templates={len(template_id_by_name)} "
      f"steps={n_steps} bullets={n_bullets} actions_used={n_actions_total} "
      f"directives={len(DIRECTIVES)}", file=sys.stderr)
