# Forge 마크다운 변환기 — 렌더러 사양 (Renderer Spec)

> 본 문서는 Forge가 [개조식 markdown spec v1.3](markdown-spec.md)을 받아
> hwpx로 변환할 때, **마크다운 요소별로 어떤 시각 출력을 생성하는지**를
> 정확히 정의합니다.
>
> **시각 spec authority**: tool2 = 금감원 오피스 프로그램의 "보고서 1"
> (= `금감원페이지` 메서드, [reference/tool2/보고서1_spec.md](../reference/tool2/보고서1_spec.md)).
> Forge 자체 정의가 필요한 부분(- · 등 tool2에 없는 항목)은 명시.

**버전**: 0.1
**최종 갱신**: 2026-04-26

---

## 1. 아키텍처 — 렌더러 단위 분리

마크다운 요소 1종 = **독립 클래스 1개** 원칙. STAGE 1 Formatter가 노드 타입에
따라 적절한 렌더러를 호출하고, **STAGE 3 Polisher의 실시간 모드 버튼**도
같은 렌더러를 그대로 호출해 활성 한/글 문서에 동일 시각을 적용한다.

```
forge/renderers/
├── __init__.py
├── base.py              # ElementRenderer 추상 base class
├── primitives.py        # 표만들기/셀여백제로/글자/문단 등 공통 COM 헬퍼
├── metadata.py          # MetadataRenderer       — 대제목 + 부서·일자 stamp
├── section.py           # SectionRenderer        — Ⅰ./Ⅱ. 중제목
├── subsection.py        # SubsectionRenderer     — 가./나./[1]/[2] 소제목
├── bullet.py            # BulletRenderer         — □ ○ - · 본문 글머리
├── annotation.py        # AnnotationRenderer     — * ※ † 주석 (단일 spec)
├── conclusion.py        # ConclusionRenderer     — => 결론 박스
├── note_callout.py      # NoteCalloutRenderer    — [참고] 박스
└── attachment.py        # AttachmentRenderer     — [붙임] 페이지 break
```

**ElementRenderer 추상 베이스**:
```python
class ElementRenderer:
    """모든 렌더러의 공통 인터페이스."""
    def __init__(self, hwp: Any, spec: ReportSpec):
        self.hwp = hwp
        self.spec = spec

    def render(self, *args, **kwargs) -> None:
        """현재 한/글 커서 위치에 요소 1개 시각 렌더링."""
        raise NotImplementedError
```

**재활용 시나리오**:
- **STAGE 1 (배치 모드)**: `formatter.py`가 파싱된 노드를 순회하며
  타입에 따라 `MetadataRenderer().render(...)`, `SectionRenderer().render(...)` 호출
- **STAGE 3 (실시간 모드)**: 탭 ③의 버튼 "참고 박스 삽입" 클릭 시
  `NoteCalloutRenderer(hwp, spec).render(text="...")` 직접 호출 — 활성 문서
  현재 커서에 박스 1개 삽입

---

## 2. 렌더러 카탈로그

### 2.1 MetadataRenderer — 대제목 + 부서·일자 stamp

**입력 (마크다운)**:
```yaml
---
보고서명: 금융결제원 PG업 등록 필요여부 검토
작성부서: 전자금융감독국 총괄팀
작성일: 2026-04-26
---
```

**출력 (시각)**:
```
┌──────────────────────────────────────────────┐
│        금융결제원 PG업 등록 필요여부 검토       │   ← 노란 배경 박스
└──────────────────────────────────────────────┘
                      (전자금융감독국 총괄팀, '26.4.26.)   ← 우정렬 stamp
```

**tool2 매핑**:
- 대제목 박스 = `금감원페이지대제목` ([decompiled.py:14245-14257](../reference/tool2/_unpacked/한컴라이브러리_decompiled.py#L14245))
- 부서·일자 stamp = `금감원페이지` 본문 14454-14460 (인라인)

**COM 액션 시퀀스**:
1. `표만들기([205-여백], [10.5])` — 1×1 셀
2. `표테두리굵기(6,6,6,6)`
3. `표배경색(250, 250, 191)` — 연노랑
4. `글자크기(17)`, `폰트('HY헤드라인M')`
5. `ParagraphShapeAlignCenter`
6. `InsertText(보고서명)`
7. `MoveRight`, `BreakPara`, `ParagraphShapeAlignJustify`
8. `줄간격(spec.line_spacing_default)`
9. `ParagraphShapeAlignRight`
10. `글자크기(12)`, `폰트('휴먼명조')`, `휴먼명조()` (7면 모두 지정)
11. `InsertText(f'({작성부서}, ’{YY}.{M}.{D}.)')`
12. `BreakPara`, `ParagraphShapeAlignJustify`

**시그니처**:
```python
class MetadataRenderer(ElementRenderer):
    def render(self, 보고서명: str, 작성부서: str, 작성일: date | str) -> None:
        ...
```

**STAGE 3 재활용**:
- 활성 문서 맨 앞에 헤더만 따로 삽입하는 버튼

---

### 2.2 SectionRenderer — 중제목 (Ⅰ./Ⅱ./...)

**입력 (마크다운)**:
```
1. 현황
2. 이슈
```

**출력 (시각)**:
```
─────────────────────────────────────────────
 Ⅰ. 현황                           ← 아래쪽만 파란 실선
```

**tool2 매핑**: `금감원페이지중제목(숫자, 내용)` ([decompiled.py:14260-14284](../reference/tool2/_unpacked/한컴라이브러리_decompiled.py#L14260))

**COM 액션 시퀀스**:
1. (커서가 줄 시작 아니면) `BreakPara`
2. `글자크기(8)`, `BreakPara` (위 빈 줄)
3. `ParagraphShapeAlignJustify`
4. `표만들기([205-여백], [8.4])`
5. `셀여백제로()`
6. `표테두리타입(0, 1, 0, 0)` — 하단만
7. `표테두리굵기(6, 8, 6, 6)`
8. `표테두리색(0, 0, 255)`
9. `글자크기(15)`, `폰트('HY견명조')`, `CharShapeBold`
10. `InsertText(roman_숫자 + '. ')` (예: 'Ⅰ. ')
11. `CharShapeNormal`
12. `글자크기(16)`, `폰트('HY헤드라인M')`
13. `InsertText(내용)`
14. `MoveRight`, `BreakPara`, `ParagraphShapeAlignJustify`

**숫자 → 로마자 변환**: 1→Ⅰ, 2→Ⅱ, ..., 10→Ⅹ. 11+ 는 그대로 표기.

**시그니처**:
```python
class SectionRenderer(ElementRenderer):
    def render(self, 번호: int, 제목: str) -> None:
        ...
    @staticmethod
    def to_roman(n: int) -> str: ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "Ⅰ. 중제목 삽입" — 다이얼로그로 번호/제목 입력 받아 호출

---

### 2.3 SubsectionRenderer — 소제목 (가./나./...)

**입력 (마크다운)**:
```
가. 개요
나. 진행상황
```

**출력 (시각)**:
```
┌─┐ ─┌────────────┐
│가│  │ 개요         │   ← 첫 셀 라벤더 배경 + 진파랑 테두리
└─┘ ─└────────────┘
```

**tool2 매핑**: `금감원페이지소제목(번호, 내용)` ([decompiled.py:14287-14316](../reference/tool2/_unpacked/한컴라이브러리_decompiled.py#L14287))

**COM 액션 시퀀스**:
1. (커서 줄 시작 아니면) `BreakPara`
2. `글자크기(8)`, `BreakPara` — 위 빈 줄
3. `ParagraphShapeAlignJustify`
4. `표만들기([7.5, 1, 49], [8.7])` — 3×1 셀
5. `표테두리색(62, 87, 165)` — 진파랑
6. `표배경색(224, 229, 250)` — 라벤더
7. `표테두리굵기(6, 6, 6, 6)`
8. `폰트('HY헤드라인M')`, `글자크기(15)`
9. `ParagraphShapeAlignCenter`, `InsertText(번호)` (예: '가')
10. `TableRightCellAppend`
11. `표테두리타입(0, 0, 1, 1)` — 좌우만 (가운데 1mm 분리 셀)
12. `TableRightCellAppend`
13. `표테두리색(62, 87, 165)`, `표테두리굵기(6, 6, 6, 6)`
14. `폰트('HY헤드라인M')`, `글자크기(15.5)`
15. `InsertText(내용)`
16. `MoveRight`, `BreakPara`, `ParagraphShapeAlignJustify`

**시그니처**:
```python
class SubsectionRenderer(ElementRenderer):
    def render(self, 번호: str, 제목: str) -> None:
        # 번호 = '가' / '나' / '1' / '2' 등 (md spec은 가나다 음절)
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "가. 소제목 삽입"

---

### 2.4 BulletRenderer — 본문 글머리 (□ ○ - ·)

**입력 (마크다운)**:
```
□ (요약) 본문
 ○ 중간 본문
  - 작은 본문
   · 더 작은 본문
```

**출력 (시각)**: 각 레벨별 글머리 + 본문, 누진 들여쓰기.
- L1 □ → `□ 본문` (휴먼명조 15pt, 내어쓰기 -22)
- L2 ○ → `◦ 본문` (휴먼명조 15pt, 내어쓰기 -33.6)
- L3 - → `- 본문` (휴먼명조 15pt, 내어쓰기 -45.2) ★ Forge 자체
- L4 · → `· 본문` (휴먼명조 15pt, 내어쓰기 -56.8) ★ Forge 자체

**tool2 매핑**:
- L1·L2 = `금감원페이지` 본문 14468-14498, 14506-14538 (인라인)
- L3·L4 = tool2 본문에는 같은 단계가 없음. Forge 자체 정의 (templates.py).

**COM 액션 시퀀스 (1 글머리 1단락)**:
1. `BreakPara` (이전 단락 종료)
2. `글자크기(spec.bullets[level-1].space_above_pt)` + `BreakPara` (위 빈 줄)
3. `폰트(spec.bullets[level-1].font)` + `휴먼명조()` (휴먼명조면 7면 보강)
4. `글자크기(spec.bullets[level-1].size_pt)`
5. `내어쓰기(spec.bullets[level-1].indent_pt)`
6. `InsertFixedWidthSpace` × `fixed_pre`
7. `InsertText(spec.bullets[level-1].out_glyph)` — 출력 글리프 (□/◦/-/·)
8. `InsertFixedWidthSpace` × `fixed_post`
9. `(요약)` 부분이 있으면: `CharShapeBold` + `폰트('맑은 고딕')` + `InsertText('('+요약+') ')` + `CharShapeNormal` + `폰트(원래 폰트)` ★ tool2 §2.5 패턴
10. `InsertText(본문)`

**시그니처**:
```python
class BulletRenderer(ElementRenderer):
    def render(self, level: int, body: str, summary: str | None = None) -> None:
        # level: 1~4 (□ ○ - ·)
        # summary: □ 의 (요약) 부분만 사용
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 4종 ("□ 큰항목 삽입", "○ 중간항목 삽입", "- 작은항목", "· 더작은")

---

### 2.5 AnnotationRenderer — 주석 (* ※ † 단일 spec)

**입력 (마크다운)**:
```
* 참조 주석 본문
※ 일반 주석 본문
† 십자가 주석 본문   (선택 — md spec엔 없으나 parser가 인식)
```

**출력 (시각)**: 모두 동일 — 맑은 고딕 12pt, 내어쓰기 -33.6, 마커 그대로.
```
       * 참조 주석 본문
       ※ 일반 주석 본문
       † 십자가 주석 본문
```

**tool2 매핑**:
- tool2의 본문 *(§2.7) + 꺽쇠박스 내부 ※(§2.9) + † 다양한 위치
- Forge는 통합: 폰트·크기·들여쓰기 일원화 (사용자 명시)

**COM 액션 시퀀스**:
1. `BreakPara`
2. `글자크기(spec.annotation.space_above_pt)` + `BreakPara`
3. `폰트(spec.annotation.font)` (= '맑은 고딕')
4. `글자크기(spec.annotation.size_pt)` (= 12)
5. `내어쓰기(spec.annotation.indent_pt)` (= -33.6)
6. `InsertFixedWidthSpace` × `fixed_pre` (= 8)
7. `InsertText(marker)` — `*` / `**` / `※` / `†` 입력 그대로
8. `InsertFixedWidthSpace` × `fixed_post` (= 2)
9. `InsertText(본문)`

**시그니처**:
```python
class AnnotationRenderer(ElementRenderer):
    def render(self, marker: str, body: str) -> None:
        # marker: '*' / '**' / '***' / '※' / '†'  — 그대로 출력
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "주석 삽입" — 다이얼로그로 마커·본문 입력

---

### 2.6 ConclusionRenderer — 결론 박스 (=>)

**입력 (마크다운)**:
```
=> 일관된 해석 기준 필요
```

**출력 (시각)**:
```
┌╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴┐
│  ⇨ 일관된 해석 기준 필요   │   ← 민트 배경 + 점선 테두리
└╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴ ╴┘
```

**tool2 매핑**: `금감원페이지점선박스` ([decompiled.py:14398-14417](../reference/tool2/_unpacked/한컴라이브러리_decompiled.py#L14398))

**COM 액션 시퀀스**:
1. (커서 줄 시작 아니면) `BreakPara`
2. `글자크기(8)`, `BreakPara` — 위 빈 줄
3. `ParagraphShapeAlignRight`
4. `표만들기([199.5-여백], [18])` — 1×1 셀
5. `표테두리타입(3, 3, 3, 3)` — 모두 점선
6. `표테두리굵기(2, 2, 2, 2)`
7. `표배경색(205, 242, 228)` — 민트
8. `폰트('휴먼명조')`, `휴먼명조()`, `글자크기(15)`
9. `InsertText('⇨ ' + 본문)`
10. `MoveRight`, `BreakPara`, `ParagraphShapeAlignJustify`

**시그니처**:
```python
class ConclusionRenderer(ElementRenderer):
    def render(self, body: str) -> None:
        # body: => 다음 텍스트. ⇨ 글머리는 자동 prepend
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "결론 박스 삽입"

---

### 2.7 NoteCalloutRenderer — 참고 박스 ([참고])

**입력 (마크다운)**:
```
[참고]
관련 법령: 전자금융거래법 §28, §29
관련 규정: 전자금융감독규정 §13
```

**출력 (시각)**: tool2 보고서1의 마지막 "참고" 박스 패턴.
```
┌──┐ ─┌──────────────────────────────┐
│참고│  │ 관련 법령: ...                │
└──┘ ─│ 관련 규정: ...                │
       └──────────────────────────────┘
```

**tool2 매핑**: `금감원페이지참고` ([decompiled.py:14420-14446](../reference/tool2/_unpacked/한컴라이브러리_decompiled.py#L14420))

**COM 액션 시퀀스** (헤더 셀):
1. `표만들기([17.6, 1, 182-여백], [8.7])` — 3×1 셀
2. `셀여백제로()`
3. `표배경색(0, 0, 255)` — 진파랑
4. `CharShapeBold`, `폰트('HY헤드라인M')`, `글자크기(15)`
5. `글자색(255, 255, 255)` — 흰색
6. `ParagraphShapeAlignCenter`, `InsertText('참고')`
7. `표오른쪽(1)`, `표테두리타입(0, 0, 1, 1)` (가운데 1mm 분리 셀)
8. `TableResizeExLeft`, `TableResizeExLeft`
9. `표오른쪽(1)`, `폰트('HY헤드라인M')`, `글자크기(15)`
10. `InsertFixedWidthSpace` × 2, `InsertText(본문 첫 줄)`

**여러 줄 처리**: 본문이 여러 줄이면 `BreakPara` 로 줄바꿈 + `InsertText` 반복.
필요 시 셀 높이 자동 확장 (한/글이 처리).

**시그니처**:
```python
class NoteCalloutRenderer(ElementRenderer):
    def render(self, lines: list[str]) -> None:
        # lines: [참고] 다음의 본문 줄 (빈 줄까지)
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "참고 박스 삽입" — 다이얼로그로 본문 입력

---

### 2.8 AttachmentRenderer — 붙임 ([붙임], [붙임 N])

**입력 (마크다운)**:
```
[붙임 1]
관련 법령 발췌

[붙임 2]
유사 사례 비교표
```

**출력 (시각)**: tool2 보고서1엔 "참고" 헤더만 있고 [붙임] 별도는 없음.
Forge 자체 정의 — **자동 페이지 break + 붙임 헤더 + 본문 영역**.

```
─────────────────── 페이지 break ───────────────────

[붙임 1] 관련 법령 발췌                  ← 헤더 (HY헤드라인M 14pt)
────────────────────────────────────────────────
관련 법령 본문...
```

**COM 액션 시퀀스**:
1. `Run('PageBreak')` — 새 페이지로
2. `폰트('HY헤드라인M')`, `글자크기(14)`, `CharShapeBold`
3. `ParagraphShapeAlignLeft`
4. `InsertText(f'[붙임 {N}] ' + 첫줄_제목)` — N 있으면 표기
5. `BreakPara`, `CharShapeNormal`
6. (가로 실선 1줄 — `Run('HorzLine')` 또는 1×1 표 1mm 굵기 6 테두리)
7. `BreakPara`
8. 본문 줄들 `InsertText(line)` + `BreakPara` 반복

**여러 [붙임 N]**: 각각 새 페이지에서 시작.

**시그니처**:
```python
class AttachmentRenderer(ElementRenderer):
    def render(self, number: int | None, lines: list[str]) -> None:
        # number: [붙임 1] → 1, [붙임] → None
        # lines: 첫 줄은 보통 제목, 이후는 본문
        ...
```

**STAGE 3 재활용**:
- 탭 ③ 버튼 "붙임 시작 (페이지 break)"

---

## 3. STAGE 1 ↔ STAGE 3 통합

### 3.1 STAGE 1 (배치 모드 = 마크다운 변환)

`forge/stage_1_formatter/hwpx_writer.py` 가 노드 리스트 순회:

```python
def generate_hwpx_via_com(hwp, doc, out_path, spec, log, mode="new"):
    if mode == "new":
        run(hwp, "FileNew")
        _apply_page_margins(hwp, spec)
        MetadataRenderer(hwp, spec).render(
            doc.metadata.보고서명,
            doc.metadata.작성부서,
            doc.metadata.작성일,
        )

    for node in doc.nodes:
        if node.type == "section":
            SectionRenderer(hwp, spec).render(int(node.marker.rstrip(".")), node.text)
        elif node.type == "subsection":
            SubsectionRenderer(hwp, spec).render(node.marker.rstrip("."), node.text)
        elif node.type == "bullet":
            level = ["□", "○", "-", "·"].index(node.marker) + 1
            BulletRenderer(hwp, spec).render(level, node.text, node.summary)
        elif node.type == "annotation":
            AnnotationRenderer(hwp, spec).render(node.marker, node.text)
        elif node.type == "conclusion":
            ConclusionRenderer(hwp, spec).render(node.text)
        elif node.type == "callout":
            if node.callout_kind == "note":
                NoteCalloutRenderer(hwp, spec).render([c.text for c in node.children])
            else:
                AttachmentRenderer(hwp, spec).render(node.callout_number,
                                                      [c.text for c in node.children])

    if mode == "new":
        _save_as_hwpx(hwp, out_path)
```

### 3.2 STAGE 3 (실시간 모드 = 활성 문서 정형조작)

`ui/tabs/realtime_tab.py` 의 버튼이 직접 렌더러 호출:

```python
def on_click_insert_section(self):
    번호 = simpledialog.askinteger("중제목", "번호 (1~12):")
    제목 = simpledialog.askstring("중제목", "제목:")
    session = self.app.ensure_hwp()
    SectionRenderer(session.hwp, self.state.spec).render(번호, 제목)

def on_click_insert_note_callout(self):
    text = simpledialog.askstring("참고 박스", "본문 (여러 줄은 \\n):")
    lines = text.split("\\n")
    session = self.app.ensure_hwp()
    NoteCalloutRenderer(session.hwp, self.state.spec).render(lines)

# ...각 요소별 1버튼
```

→ **렌더러 한 벌로 두 모드 완전 커버**.

---

## 4. 공통 헬퍼 (forge/renderers/primitives.py)

tool2의 `한컴라이브러리.기본한컴` 의 자주 쓰이는 메서드들을 함수로 (재구현):

| 함수 | tool2 메서드 | 용도 |
|---|---|---|
| `make_table(hwp, cols_mm, rows_mm)` | `표만들기(가로크기, 세로크기)` | 표 생성 |
| `set_cell_margin_zero(hwp)` | `셀여백제로()` | 표 셀 여백 0 |
| `set_table_border_type(hwp, top, bottom, left, right)` | `표테두리타입(상,하,좌,우)` | 외곽 테두리 종류 (0=없음, 1=실선, 3=점선) |
| `set_table_border_thickness(hwp, t, b, l, r)` | `표테두리굵기(...)` | 굵기 |
| `set_table_border_color(hwp, r, g, b)` | `표테두리색(빨강,초록,파랑)` | 테두리 색 |
| `set_table_bg(hwp, r, g, b)` | `표배경색(빨강,초록,파랑)` | 셀 배경 |
| `set_font(hwp, font, size_pt, bold)` | `폰트` + `글자크기` + (휴먼명조면 7면 모두) | 폰트 일괄 |
| `set_text_color(hwp, r, g, b)` | `글자색(빨강,초록,파랑)` | 글자색 |
| `set_indent(hwp, pt)` | `내어쓰기(값)` | 내어쓰기 |
| `set_line_spacing(hwp, pct)` | `줄간격(간격)` | 줄간격 (%) |
| `insert_fixed_space(hwp, count=1)` | `Run('InsertFixedWidthSpace')` × N | 고정폭 공백 |
| `break_para(hwp)` | `Run('BreakPara')` | 단락 break |
| `align(hwp, mode)` | `Run('ParagraphShapeAlign{Left/Center/Right/Justify}')` | 정렬 |
| `move_right(hwp)` | `Run('MoveRight')` | 표 셀 탈출용 |
| `move_table_right(hwp, count=1)` | `표오른쪽(이동)` | 표 내 셀 이동 |

→ 렌더러는 이 헬퍼들 + `forge/com_helpers.py:set_param()` 만 호출.
   tool2 한컴라이브러리 wrapper 411개 모두 만들지 않음 — 필요한 30~40개만.

---

## 5. 구현 로드맵

### Phase 1 — 골격 + 1 렌더러 PoC
1. `forge/renderers/base.py` — `ElementRenderer` 추상 클래스
2. `forge/renderers/primitives.py` — 위 §4 헬퍼 함수 30~40개
3. `forge/renderers/metadata.py` — `MetadataRenderer` (가장 간단한 것부터)
4. `hwpx_writer.py` 에서 메타데이터 부분만 신 렌더러로 교체 + 동작 검증

### Phase 2 — 나머지 렌더러
순서: `bullet → annotation → section → subsection → conclusion → callout → attachment`
(빈도 높은 것부터)

### Phase 3 — STAGE 3 통합
- `realtime_tab.py` 의 placeholder 버튼들을 렌더러 호출로 wire
- 다이얼로그 박스 1개 (입력 받는 단순 모달)

### Phase 4 — 검증·튜닝
- 실제 한/글에서 각 렌더러 동작 확인
- tool2 출력과 시각 비교 (직접 두 hwpx 열어 비교)
- 디테일 차이 (테두리 굵기 1·2 차이, 색상 미세 등) 보정

---

## 6. 의존성 명세

각 렌더러가 사용하는 외부:

| 렌더러 | 의존 |
|---|---|
| MetadataRenderer | `set_param`, `make_table`, `set_table_*`, `set_font`, `align`, `insert_text`, `set_line_spacing` |
| SectionRenderer | 〃 + `set_cell_margin_zero`, `set_table_border_type`, `set_table_border_color` |
| SubsectionRenderer | 〃 + `move_table_right` |
| BulletRenderer | `set_param`, `set_font`, `set_indent`, `insert_fixed_space`, `insert_text`, `break_para` |
| AnnotationRenderer | 같음 (BulletRenderer 와 거의 동일 패턴) |
| ConclusionRenderer | MetadataRenderer 와 같음 |
| NoteCalloutRenderer | SubsectionRenderer 와 같음 (3-셀 표 패턴) |
| AttachmentRenderer | `set_param`, `Run('PageBreak')`, `set_font`, `insert_text`, `break_para` |

**MCP 활용 (개발 시점만)**:
- `tool2-spec-mcp.get_tool2_method_source("금감원페이지중제목")` — 정확 COM 시퀀스 확인
- `tool2-spec-mcp.get_tool2_template("금감원페이지")` — 전체 호출 순서
- `hwp-api-mcp.search_hwp_action("CharShape")` — 액션 항목 확인
- `hwp-api-mcp.get_hwp_parameterset(<id>)` — 파라미터 세부

→ 룰 작성 시 위 MCP로 매번 검증, 결과는 결정론적 코드로만 남김 (runtime LLM·MCP ✗).

---

## 7. 향후 확장

- **다른 4종 템플릿** (금감보고서/금감업무정보/금감보도자료/금감원장보고)
  → 같은 렌더러 8종에 다른 spec(폰트/색상/박스 크기) 만 주입하면 동작.
  spec/templates.py 에 `REPORT2_SPEC`, `BUSINESS_INFO_SPEC` 등 추가만 하면 됨.

- **사용자 커스텀 spec**: 탭 ① "보고서 템플릿" 콤보박스에 사용자 spec 저장·불러오기.

- **새 요소 추가**: 새 마크다운 마커 추가 시 `forge/renderers/<new>.py` 1 파일 추가
  + parser.py 에 패턴 1줄 추가 + dispatcher 1행 추가.

---

*작성: 2026-04-26*
