# 표 렌더러 (TableRenderer) 도입 개발 계획

> **상태**: 구현 완료 (2026-05-18). 본 문서는 의사결정 이력 + 작업 계획의
>           아카이브. 현행 spec 은 [markdown-spec.md §9](markdown-spec.md#9-표-gfm-부분집합)
>           및 [renderer-spec.md §2.9](renderer-spec.md#29-tablerenderer--표) 가 권위.
>           Phase 4 (사용자 산출물 18개 표 시각 검증) 는 GUI 변환 후 별도 수행.
> **저자 시점**: 2026-05-18 (의사결정 갱신: 2026-05-18)
> **연관 spec**: [markdown-spec.md](markdown-spec.md), [renderer-spec.md](renderer-spec.md)
> **연관 코드**: [forge/formatter/parser.py](../forge/formatter/parser.py),
>                 [forge/renderers/primitives.py](../forge/renderers/primitives.py)
> **MCP 권위 사례**: tool2 `행안부초록표` (line 3053+) — 다행 N×M 표의 1:1 권위 패턴.
>                   `표만들기` (line 736-751), `표탈출` (line 913-918), `셀여백제로`
>                   (line 462-474) 도 권위 spec.

본 계획이 구현 완료되면 본 파일은 [renderer-spec.md](renderer-spec.md) §2.9 로
흡수되고, 입력 사양 절은 [markdown-spec.md](markdown-spec.md) §10 으로
이관된다.

## 0. 의사결정 결과 (2026-05-18 사용자 확정)

| # | 결정 | 값 | 근거 |
|:-:|---|---|---|
| **D-탈출** | 표 탈출 패턴 | `escape_table()` 신규 primitive (`CloseEx + MoveDown + CloseEx`) | tool2 `표탈출` (line 913-918) 권위. 기존 4 렌더러는 1×1·1×N 이라 `move_right` 만으로 동작, 다행 표는 권위 패턴 필요. |
| **D3 (재)** | 데이터 셀 폰트 크기 | **휴먼명조 12pt** | 본문 15pt 대비 한 단계 작아 시각 위계 자연. 산출물 #6·#9 의 8행 표가 한 페이지에 들어감. |
| **D-padding** | 셀 좌우 padding | **0mm** (셀여백제로만) | tool2 권위 — 모든 tool2 표 사례가 셀 padding 0. |
| **D-실시간** | 실시간 모드 표 지원 | **v1 같이 포함** | `convert_selection_to_hwpx` 는 dispatcher 공유 — table 분기 1줄로 자동 지원. |
| D1 | 1열 라벨 폭 | 25mm 고정 | 산출물 18개 표 모두 1열 라벨 짧음. |
| D2 | 2~N열 폭 | 균등 분배 | 단순·예측 가능. |
| D5 | 셀 내부 줄바꿈 | v1 미지원 | 산출물에 없음. |
| D6 | 본문 폭 초과 시 가로 회전 | v1 미지원 | 본문 폭에 고정. |
| D7 | 헤더 행 배경색 | 라벤더 RGB(224,229,250) | subsection 마커 셀과 일관. |
| T4 | 셀 수 초과 | 무시 + log 경고 | 데이터 손실 사실을 사용자가 인지. |

---

## 1. 목표 — 산출물 호환 범위

대상은 사용자가 `’26.5.13. 금융위 회신 대조표` 형태로 산출한 **실제 검토
보고서 1편** 안의 표 18개. 이 보고서는 LLM 이 GFM `|...|` 표 문법으로
출력하는 전형 사례이며, 본 표들이 한/글 표로 1:1 변환되면 다른 유사
산출물도 동일 메커니즘으로 커버된다.

### 1.1 산출물 표 18개 분류

| # | 사안 | 헤더 패턴 | 데이터 행 | 특이사항 |
|:-:|---|---|:-:|---|
| 1 | 1-1 시행령 §5② | 구분 / 현행 / 금결원 1차 / 금융위안 | 1 | 「」 책 인용 |
| 2 | 2-1 §13의8① | 구분 / 현행 / 금결원 2차 / 금융위안 | 1 | ① ② ③ 원숫자 |
| 3 | 2-2 §13의8②③④ | 구분 / 현행 / 금결원 / 금융위안 | 3 | 라벨에 ②③④ |
| 4 | 2-3 §13의9 | 구분 / 현행 / 금결원 / 금융위안 | 2 | `→`, 셀 긴 텍스트 |
| 5 | 2-4 §13의10 | 구분 / 현행 / 금결원 / 금융위안 | 1 | — |
| 6 | 2-5 §13의11 | 구분 / 현행 / 금결원 2차 / 금융위안 | **8** | 최대 행 수 |
| 7 | 2-6 §13의12·§62·§56의2 | 구분 / 현행 / 금결원 / 금융위안 | 3 | — |
| 8 | 2-7 §13의13 | 구분 / 현행 / 금결원 / 금융위안 | 3 | — |
| 9 | 3-1 §17③④ | 구분 / 현행 / 금감원 3차 / 금융위안 | **8** | 헤더에 `금감원` |
| 10 | 3-2 §20의2 | 구분 / 현행 / 금감원 3차 / 금융위안 | 4 | — |
| 11 | 4-1 §22의6 | 구분 / 현행 / 금감원 3차 / 금융위안 | 4 | `(개정법 §36의3①)` 괄호 |
| 12 | 4-2 §23의2·§62의2 | 구분 / 현행 / 금감원 3차 / 금융위안 | 4 | — |
| 13 | 4-3 §57 | 구분 / 현행 / 금감원 4차 / 금융위안 | 1 | 단일 행 |
| 14 | 5-1 §63의2 | 구분 / 현행 / 금감원 4차 / 금융위안 | 5 | — |
| 15 | 5-2 별표2 | **위반행위** / 현행 / 금감원 / 금융위안 | 5 | 첫 열 라벨 변경 |
| 16 | 5-3 §24의2 | 구분 / 현행 / 금감원 / 금융위안 | 1 | — |
| 17 | 6 별표3 | **위반행위** / 현행 / 금감원 5차 / 금융위안 | 5 | — |
| 18 | 7 §30① | **위탁업무** / 현행 / 금감원 / 금융위안 | 5 | — |

### 1.2 도출되는 공통 요구사항

| 요구 | 내용 |
|---|---|
| 열 수 | 모두 4열 (산출물 전체 패턴 — 다른 열 수도 일반화 지원) |
| 헤더 행 | 1행 |
| 정렬 표기 | `---` 만 사용 (`:---:` 정렬 표기 없음) — 일반 지원은 하되 산출물엔 불요 |
| 데이터 행 수 | 1~8행 (가변) |
| 셀 텍스트 | 최대 약 200자, 평균 30~80자 |
| 셀 내부 줄바꿈 | 없음 (한/글 자동 wrap 으로 충분) |
| 특수문자 | `§ ① ② ③ ④ ⑤ ⑥ ⑦ ⑧ ⑨ ⑩` `「」` `『』` `'` `→` `·` — 모두 InsertText 로 직접 가능 |
| 인라인 강조 | 산출물 표 안에는 `__bold__` 없음. v1 미지원, v2 에서 추가 |

---

## 2. 입력 사양 (markdown-spec.md §10 신설안)

### 2.1 기본 형식 — GFM 부분집합

```
| 헤더1 | 헤더2 | 헤더3 |
|---|---|---|
| 데이터1 | 데이터2 | 데이터3 |
| 데이터4 | 데이터5 | 데이터6 |
```

- **헤더 행 필수** — 첫 줄이 `|...|` 형식.
- **둘째 줄은 구분선** — `|---|---|...|` 또는 정렬 표기 `|:---:|---:|:---|`.
  구분선 누락 시 표로 인식하지 않고 본문 텍스트로 fallback.
- **데이터 행 0개 허용** — 헤더만 있는 표도 정상 처리 (드물지만 spec 정의).
- **표 종료 조건** — 빈 줄, 또는 `|` 로 시작하지 않는 줄, 또는 문서 끝.
- **셀 내부 줄바꿈** v1 미지원. v2 에서 `<br>` 토큰 또는 `\\n` 도입 검토.
- **인라인 강조** v1 미지원. v2 에서 `__X__` (md spec §6 과 동일) 검토.

### 2.2 정렬 표기 (v1 지원, 산출물엔 사용 안 됨)

| 표기 | 의미 |
|---|---|
| `---` | 기본 (좌정렬) |
| `:---` | 좌정렬 |
| `---:` | 우정렬 |
| `:---:` | 가운데 정렬 |

→ HWP `ParagraphShapeAlign{Left/Right/Center}` 로 매핑.

### 2.3 표 식별 규칙

- 라인 시작이 `|` 이고 같은 라인에 `|` 가 2개 이상 → 표 후보 행.
- 첫 표 후보 행 + 다음 줄이 구분선 패턴 → 표 시작.
- 글머리(`□`/`○`/`-`/`·`) 와 충돌 없음 — 글머리는 `|` 로 시작하지 않음.

### 2.4 markdown-spec 변경 이력 추가 (예정)

| 버전 | 일자 | 변경 |
|:-:|:-:|---|
| 1.6 | TBD | **표 신설 (§10)** — GFM 부분집합. 헤더 1행 + 구분선 + 데이터 N행. 정렬 4종 지원. 셀 내 줄바꿈/강조는 v2 |

---

## 3. parser.py 변경

### 3.1 새 Node 타입

```python
NodeType = Literal[
    "section", "subsection", "bullet", "annotation",
    "conclusion", "callout", "blank",
    "table",       # ★ 신규
]

@dataclass
class TableNode(Node):
    """type='table' 전용 필드."""
    headers: list[str] = field(default_factory=list)
    rows: list[list[str]] = field(default_factory=list)
    aligns: list[str] = field(default_factory=list)  # 'left'|'center'|'right'
```

기존 `Node` 가 `dataclass` 라 필드 추가 또는 separate dataclass 선택. 권장:
`Node` 에 `headers`/`rows`/`aligns` 옵셔널 필드 직접 추가 (다른 노드 타입과
일관성).

### 3.2 새 정규식

```python
TABLE_ROW_RE = re.compile(r"^\s*\|(.+)\|\s*$")
TABLE_SEP_RE = re.compile(r"^\s*\|(\s*:?-{3,}:?\s*\|)+\s*$")
```

### 3.3 `_parse_body` 분기 추가

```python
# callout 분기 이후, 단일 라인 노드 분기 이전:
if TABLE_ROW_RE.match(line) and i + 1 < len(lines):
    next_line = lines[i + 1].strip()
    if TABLE_SEP_RE.match(next_line):
        table_node, consumed = _parse_table(lines, i)
        nodes.append(table_node)
        i += consumed
        continue
```

### 3.4 `_parse_table()` 함수

```python
def _parse_table(lines: list[str], start: int) -> tuple[Node, int]:
    """헤더+구분선+데이터행 N개를 Node(type='table') 1개로."""
    headers = _split_row(lines[start])
    aligns = _parse_aligns(lines[start + 1])
    rows: list[list[str]] = []
    j = start + 2
    while j < len(lines):
        line = lines[j].strip()
        if not line or not TABLE_ROW_RE.match(line):
            break
        rows.append(_split_row(line))
        j += 1
    return Node(type="table", headers=headers, rows=rows, aligns=aligns), j - start

def _split_row(line: str) -> list[str]:
    inner = line.strip().strip("|")
    return [cell.strip() for cell in inner.split("|")]

def _parse_aligns(sep_line: str) -> list[str]:
    cells = _split_row(sep_line)
    out = []
    for c in cells:
        left = c.startswith(":")
        right = c.endswith(":")
        out.append("center" if (left and right) else
                   "right" if right else
                   "left")
    return out
```

### 3.5 테스트 케이스 (parser 단위)

- T1: 헤더만 — 정상 파싱, rows 빈 리스트.
- T2: 헤더 + 구분선 + 데이터 1행 — `Node(type='table')` 생성.
- T3: 데이터 행 셀 수 < 헤더 셀 수 — 빈 문자열로 패딩.
- T4: 데이터 행 셀 수 > 헤더 셀 수 — **무시 + `log` 경고 emit** (사용자가
  데이터 손실 인지). v1 확정 정책.
- T5: 헤더는 있는데 다음 줄이 구분선 아님 — 표가 아닌 본문 텍스트로 처리.
- T6: 표 직후 빈 줄 → 표 종료.
- T7: 표 직후 `□` 글머리 → 표 종료.
- T8: 산출물의 18개 표를 모두 파싱하여 headers/rows 개수 정확 확인.

---

## 4. TableRenderer 신규 (forge/renderers/table.py)

### 4.1 시그니처

```python
class TableRenderer(ElementRenderer):
    def render(
        self,
        headers: list[str],
        rows: list[list[str]],
        aligns: list[str] | None = None,
    ) -> None:
        """현재 캐럿 위치에 표 1개 삽입."""
```

### 4.2 시각 디자인

| 요소 | 값 | 근거 |
|---|---|---|
| 표 가로 너비 | `205 - 양옆 여백 mm` (= ReportSpec.page_width_mm 기반) | 본문 폭과 동일 |
| 1열 (라벨) 폭 | 25mm 고정 | 산출물 첫 열이 모두 짧은 라벨 (10~15자) |
| 2~N열 폭 | (전체 - 25) ÷ (N-1) 균등 | 산출물 패턴 |
| 행 높이 | `auto` (한/글 자동 확장) | 셀 본문 길이 가변 |
| 셀 여백 | `셀여백제로` 단독 (좌우 0mm) — D-padding | tool2 권위 (`셀여백제로` line 462-474). 모든 tool2 표 사례 0. |
| 테두리 색 | RGB(62, 87, 165) — 진파랑 | tool2 `금감원페이지소제목` 헤더 셀 |
| 테두리 굵기 | 6 (0.6pt 균등) | 동일 |
| 헤더 셀 배경 | RGB(224, 229, 250) — 라벤더 (D7) | subsection 마커 셀과 동일 |
| 헤더 셀 폰트 | HY헤드라인M 12pt, 가운데 정렬 | tool2 `행안부초록표` 헤더 셀 패턴 (`문장풀(..., 0, 1, ...)` 4번 인자=1=가운데) |
| 데이터 셀 배경 | 흰색 (배경 미설정) | tool2 `행안부초록표` 데이터 셀 — 배경 호출 안 함 |
| 데이터 셀 폰트 | **휴먼명조 12pt**, 좌정렬 (또는 `aligns` 따름) | D3 — 본문 15pt 대비 한 단계 축소. tool2 `문장풀(..., 0, 0, ...)` 4번 인자=0=좌정렬 패턴. |

### 4.3 열 폭 산정 정책

산출물 표 18개를 보면 첫 열은 항상 짧은 라벨, 나머지 열은 본문. 첫 열
고정 25mm + 나머지 균등 분배가 무난.

`ReportSpec.table` 에 다음 dataclass 추가:
```python
@dataclass
class TableStyle:
    label_col_mm: float = 25.0
    border_color: tuple[int, int, int] = (62, 87, 165)
    border_thick: int = 6
    header_bg: tuple[int, int, int] = (224, 229, 250)
    header_font: str = "HY헤드라인M"
    header_size_pt: float = 12.0
    body_font: str = "휴먼명조"
    body_size_pt: float = 12.0   # D3: 본문 15pt 대비 한 단계 축소
    # cell padding 은 0 (tool2 권위) — 별도 필드 없음. set_cell_margin_zero 만 호출.
```

### 4.4 COM 액션 시퀀스 — tool2 `행안부초록표` (line 3053+) 권위 패턴

★ 표 앞 위 빈 줄(8pt)과 이전 단락 BreakPara 는 **dispatcher (`_dispatch_nodes`,
[hwpx_writer.py:216-244](../forge/formatter/hwpx_writer.py#L216-L244)) 가 자동
prepend** — TableRenderer 내부에서 emit 금지 (다른 렌더러와 동일 규약,
[note_callout.py:36](../forge/renderers/note_callout.py#L36) 주석 참조).

1. `make_table(cols_mm=[25, w2, w3, w4], rows_mm=[8.4] * (1 + len(rows)))`
2. `set_cell_margin_zero()` — D-padding 결정에 따라 padding 0 단독
3. `set_table_border_color(*spec.table.border_color)`
4. `set_table_border_thickness(6, 6, 6, 6)`
5. **헤더 행 순회**: 각 셀에 대해
   - `set_table_bg(*spec.table.header_bg)`
   - `set_font(spec.table.header_font, spec.table.header_size_pt)`
   - `align('center')` — tool2 `문장풀(..., 1, ...)` 4번 인자=1 등가
   - `insert_text(header_cell)`
   - `move_table_right()` — 마지막 셀도 호출 (자동으로 다음 행 첫 셀 진입,
     tool2 `행안부초록표` 패턴 확인 line 3053-3072)
6. **데이터 행 순회**: 각 행, 각 셀에 대해
   - 헤더 행 배경 잔류 방지 위해 데이터 셀에선 `set_table_bg` 호출 안 함
     (tool2 권위 — `행안부초록표` 가 데이터 셀에서 배경 호출 생략)
   - `set_font(spec.table.body_font, spec.table.body_size_pt)`
   - `align(aligns[col] or 'left')` — tool2 `문장풀(..., 0, ...)` 4번 인자=0
   - `insert_text(cell_text)`
   - `move_table_right()`  ※ 마지막 행 마지막 셀에서도 호출하면 새 행이 자동
     생성될 위험 — **마지막 셀에선 호출 생략**.
7. `escape_table()` — 신규 primitive (§4.5 참조). tool2 `표탈출` (line 913-918)
   = `Run('CloseEx') + Run('MoveDown') + Run('CloseEx')`.

### 4.5 헬퍼 부족분 — MCP 검증 결과 (2026-05-18)

**기존 충분 헬퍼** (신규 작업 0):
- `make_table(cols_mm, rows_mm)` — [primitives.py:331](../forge/renderers/primitives.py#L331).
  tool2 `표만들기` (line 736-751) 와 100% 일치 (`TreatAsChar=1` 포함).
- `set_cell_margin_zero()` — [primitives.py:356](../forge/renderers/primitives.py#L356).
  tool2 `셀여백제로` (line 462-474) 와 100% 일치.
- `set_table_border_color(r,g,b)` — [primitives.py:393](../forge/renderers/primitives.py#L393).
  `BorderCorlorLeft` sic 처리됨.
- `set_table_border_thickness(t,b,l,r)` — [primitives.py:382](../forge/renderers/primitives.py#L382).
- `set_table_bg(r,g,b)` — [primitives.py:431](../forge/renderers/primitives.py#L431).
- `move_table_right(count=1)` — [primitives.py:462](../forge/renderers/primitives.py#L462).
  tool2 `표오른쪽` = `Run('TableRightCellAppend')` 와 100% 일치.

**신규 추가 필요 헬퍼 1개**:
```python
def escape_table(hwp: Any) -> None:
    """표 탈출 — tool2 `표탈출` (line 913-918) 1:1 재현.

    `CloseEx → MoveDown → CloseEx` 3액션. 다행 표 캐럿이 표 밖으로
    완전히 빠져나오게 한다. 기존 4 렌더러가 사용하는 `move_right + break_para`
    는 1×1·1×N 표에서 우연히 동작 — 다행 표는 본 권위 패턴 필수.
    """
    hwp.HAction.Run("CloseEx")
    hwp.HAction.Run("MoveDown")
    hwp.HAction.Run("CloseEx")
```

위치: [primitives.py](../forge/renderers/primitives.py) 의 `move_table_right`
바로 아래 (§"표" 섹션 안).

**MCP 검증 완료** — 다음 4건은 본 plan 작성 시점에 검증됨, 구현 시 재검증 불요:
1. ✅ `mcp__tool2-spec__get_tool2_method_source("표만들기")` (line 736-751) → `make_table` 와 일치
2. ✅ `mcp__tool2-spec__get_tool2_method_source("표탈출")` (line 913-918) → `escape_table` 신설 근거
3. ✅ `mcp__tool2-spec__get_tool2_method_source("행안부초록표")` (line 3053+) → §4.4 셀별 시퀀스 권위
4. ✅ `mcp__tool2-spec__get_tool2_action_usage("TableRightCellAppend")` → 57개 메서드가 동일 패턴

### 4.6 hwpx_writer.py dispatcher 추가

```python
elif node.type == "table":
    TableRenderer(hwp, spec).render(node.headers, node.rows, node.aligns)
```

### 4.7 실시간 모드 (Ctrl+Shift+X) — v1 자동 지원 (D-실시간)

[hwpx_writer.py:341-439](../forge/formatter/hwpx_writer.py#L341-L439)
`convert_selection_to_hwpx` 는 selection 의 plain text 를 `GetTextFile` 로
추출 → `parse_markdown` → `generate_hwpx_via_com(mode='cursor')` 호출.
`mode='cursor'` 도 `_dispatch_nodes` 를 공유하므로 §4.6 의 table 분기 1줄
추가로 자동 지원된다. selection 텍스트에 `|...|` 가 있으면 parser 가 표로
인식하고 그 자리에 변환 출력.

별도 코드 작업 없음 — Phase 4 검증에 selection-mode 표 1건 추가만.

---

## 5. 단계별 작업 항목

### Phase 1 — Parser (반나절)

1. [forge/formatter/parser.py](../forge/formatter/parser.py) 에 정규식·`_parse_table()`·`_split_row()`·`_parse_aligns()` 추가
2. `_parse_body()` 분기 추가
3. `Node` 에 `headers`/`rows`/`aligns` 필드 추가
4. parser 단위 테스트 (§3.5 T1~T8) — `tests/test_parser_table.py` 신규
5. 산출물 본문 전체를 parser 에 통과시켜 18개 표 모두 정상 인식 확인
   (assertion: `[n for n in doc.nodes if n.type=='table']` 길이 == 18)

### Phase 2 — Primitives 보강 (30분)

1. ~~MCP 의무 검증~~ — §4.5 에 완료 결과 기록됨, skip.
2. `escape_table(hwp)` 1 함수 신설 ([primitives.py](../forge/renderers/primitives.py)
   의 `move_table_right` 바로 아래). 주석에 tool2 line 913-918 출처 명시.
3. `make_table` + `escape_table` 단독 호출 PoC — 빈 한/글에 4×3 표 1개 삽입 후
   캐럿이 표 밖으로 정확히 빠져나오는지 확인.

### Phase 3 — TableRenderer (반나절)

1. [forge/renderers/table.py](../forge/renderers/table.py) 신규
2. [forge/formatter/templates.py](../forge/formatter/templates.py) 에
   `TableStyle` dataclass 추가, `ReportSpec` 에 `table: TableStyle` 필드 추가
3. [forge/formatter/hwpx_writer.py](../forge/formatter/hwpx_writer.py)
   dispatcher 에 `table` 분기 1줄 추가
4. 단일 표 PoC (산출물 표 #1: 1-1 시행령 §5②) 한/글에 출력 후 시각 확인

### Phase 4 — 산출물 전체 통과 검증 (반나절)

1. 산출물 markdown 전체를 GUI 탭 ③ 에 붙여넣고 변환
2. 18개 표 모두 hwpx 에 정상 출력 확인. 다음 항목 체크:
   - 4열 그리드 정확 (열 수 누락 없음)
   - 데이터 행 수 일치 (특히 8행 표 2개: 2-5, 3-1)
   - 셀 텍스트 잘림 없음 (가장 긴 셀 = 1-1 의 「전기통신사업법」… 약 100자)
   - 특수문자 `§ ① 「」 →` 깨짐 없음
   - 헤더 배경(라벤더) 적용 확인
3. 표 위/아래 빈 줄 시각 균형 확인

### Phase 5 — 회귀·문서화 (1~2시간)

1. 기존 산출물(표 없는 보고서) 변환 결과에 회귀 없음 확인
2. [markdown-spec.md](markdown-spec.md) §10 표 절 추가 + 변경 이력 1.6 추가
3. [renderer-spec.md](renderer-spec.md) §2.9 TableRenderer 절 추가
4. 본 plan 파일은 §2 §4 내용을 위 두 spec 에 흡수한 후 삭제 또는 `archive/` 로 이동
5. [CLAUDE.md](../CLAUDE.md) §3.4 디렉토리 구조 표에 `table.py` 1행 추가
6. [TASK_LOG.md](../TASK_LOG.md) append

**예상 총 소요**: 1.5~2 일 (MCP 검증 + 단일 표 PoC 결과에 따라 ±0.5일).

---

## 6. 결정 사항 — §0 으로 이관됨

모든 결정 항목은 본 문서 §0 "의사결정 결과" 표에 통합. 본 절은 placeholder.

---

## 7. v2 확장 (별도 계획)

- 셀 내부 `<br>` 줄바꿈 → 셀 내 `BreakPara`
- 셀 내부 `__X__` bold → `CharShapeBold` + `CharShapeNormal` 토글
- 셀 내부 `*` 참조 주석 마커 → 표 아래 자동 주석 모음
- 가로 페이지 자동 회전 (표 폭 > 본문 폭일 때)
- 셀 자동 병합 (`^` 마커로 위 셀과 병합 등 GFM 확장 문법)
- 표 → 본문 selection (Ctrl+Shift+X) 흐름의 표 인식 (실시간 모드)

---

## 8. 산출물 18개 표 — 출력 검수 체크리스트

Phase 4 변환 후 hwpx 파일에서 표 1개씩 시각 확인 시 사용.

- [ ] **표01** 1-1 시행령 §5② — 4열 × 2행 (헤더+1)
- [ ] **표02** 2-1 §13의8① — 4열 × 2행
- [ ] **표03** 2-2 §13의8②③④ — 4열 × 4행
- [ ] **표04** 2-3 §13의9 — 4열 × 3행
- [ ] **표05** 2-4 §13의10 — 4열 × 2행
- [ ] **표06** 2-5 §13의11 — 4열 × **9행** (헤더+8)
- [ ] **표07** 2-6 §13의12·§62·§56의2 — 4열 × 4행
- [ ] **표08** 2-7 §13의13 — 4열 × 4행
- [ ] **표09** 3-1 §17③④ — 4열 × **9행**
- [ ] **표10** 3-2 §20의2 — 4열 × 5행
- [ ] **표11** 4-1 §22의6 — 4열 × 5행
- [ ] **표12** 4-2 §23의2·§62의2 — 4열 × 5행
- [ ] **표13** 4-3 §57 — 4열 × 2행
- [ ] **표14** 5-1 §63의2 — 4열 × 6행
- [ ] **표15** 5-2 별표2 — 4열 × 6행 (1열 라벨 = `위반행위`)
- [ ] **표16** 5-3 §24의2 — 4열 × 2행
- [ ] **표17** 사안 6 별표3 — 4열 × 6행 (1열 라벨 = `위반행위`)
- [ ] **표18** 사안 7 §30① — 4열 × 6행 (1열 라벨 = `위탁업무`)

체크 시 확인 항목: 열 수·행 수·셀 텍스트 무손실·특수문자 무손상·헤더 색상·테두리 일관성·표 위·아래 빈 줄.

---

*작성: 2026-05-18*
