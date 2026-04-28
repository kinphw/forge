# Forge — AI Collaboration Guide

> 본 프로젝트에서 작업하는 모든 AI 협업 도구(Claude, GPT 등)의 진실 원천입니다.
> 변경이 발생하면 코드보다 먼저 이 문서를 갱신합니다.

---

## 1. 프로젝트 개요

**Forge** 는 개조식 markdown 을 한/글 보고서(.hwpx 산출 / .hwp 호환 입력)로
변환하고, 활성 한/글 문서를 직접 정형 조작하는 **LLM-free HWP 툴킷**입니다.

### 핵심 가치

- **LLM-free runtime**: 룰 실행 시점에 LLM 호출 0. 결정론적·재현 가능.
- **tool2 시각 동등**: 산출물 시각은 금감원 공식 도구(tool2) 출력과 동등해야
  함. tool2 가 시각 spec authority — 자세한 내용 §3.2.
- **단일 진입점**: GUI 한 윈도우에서 두 사용 모드를 모두 처리. CLI 미제공.
- **결정 단순화**: 사용자에게 노출되는 인터페이스는 단 2 모드. 내부 구현 분리는
  관심사 분리·테스트 가능성 목적이지 사용자가 알 필요 없음.

### 운용 전제

- 모든 운용 PC 에 한/글 설치되어 있다고 가정. STAGE 전체가 한/글 COM
  (`HwpFrame.HwpObject`) 위에서 동작.
- DRM(Fasoo 등) 환경 호환 — 신규 spawn 은 ShellExecute(`os.startfile`) 우선,
  CoCreate fallback. (한 단계 자세한 내용은 [forge/hwp_session.py](forge/hwp_session.py) 의 docstring.)

---

## 2. 사용 모드 (사용자 관점)

GUI 한 윈도우 안에 3 탭. 핵심 동선은 두 모드:

| 모드 | UI 위치 | 동작 |
|---|---|---|
| **배치** — md → 새 hwpx | 탭 ③ 마크다운 입력 | 사용자가 개조식 md 를 좌측에 입력 → "변환" 클릭 → 새 .hwpx 파일 생성 |
| **실시간** — 활성 문서 정형 | 탭 ① 개별 작업 | 사용자가 한/글에서 작업 중인 문서에 룰 1개씩 적용 (단축키 또는 버튼) |

탭 ② 는 보고서 양식 spec(폰트·여백·글머리 정의) 편집용 — 두 모드 공통 입력.

### 실시간 모드의 시스템 전역 단축키 (Win32 RegisterHotKey)

| 단축키 | 동작 |
|---|---|
| Ctrl+Shift+Q | 자동 정렬 (들여쓰기 → 자간 → 들여쓰기 3 단계 연속) |
| Ctrl+Shift+W | 어절 1 개 끌어올림 (자간 좁힘) |
| Ctrl+Shift+A | 폰트·크기 1 적용 (사용자 입력 칸의 값) |
| Ctrl+Shift+S | 폰트·크기 2 적용 |
| Ctrl+Shift+D | 현재 문단 글자 크기 (빈줄 자간 꼬임 회피용 작은 크기) |
| Ctrl+Shift+Z | 자간 0 초기화 |
| Ctrl+Shift+X | **선택 영역 → 마크다운 변환** (한/글 selection 의 plain text 를 md 로 해석해 그 자리에 변환 출력) |

각 hotkey 의 letter 는 탭 ① UI 의 Entry 칸에서 사용자가 임의 변경 가능
(`replace`).

---

## 3. 아키텍처

### 3.1 파이프라인 (개념상 2 단계)

```
입력: 개조식 md (사용자 타이핑 / Ctrl+Shift+X 의 selection text)
        │
        ▼
┌──────────────────────────────────┐
│  formatter — md → 한/글 본문     │  forge/formatter/
│  parser → 노드 → 렌더러 dispatch │  ★ 8 종 ElementRenderer 호출
└──────────────┬───────────────────┘
               ▼
┌──────────────────────────────────┐
│  linter — 정형 룰 호출           │  forge/linter/
│  자간조정·들여쓰기 정렬·자간      │  ★ 두 진입점:
│  reset·어절 끌어올림 등           │   ─ batch: 문서 전체 순회
│                                  │   ─ realtime: 현재 문단 / selection 영역
└──────────────────────────────────┘
출력: .hwpx (배치) / 입력 형식 보존 (실시간)
```

원래 설계의 STAGE 3 polisher 는 별도 코드 패키지로 분리하지 않음 —
자간·들여쓰기·자간 reset 등 룰들이 모두 동일한 `InitScan` / `GetText` /
단일 문단 알고리즘 기반이라 [linter/](forge/linter/) 안에 자연스럽게
흡수됨. 개념상 "단일 룰 폴리싱" 도 linter 의 한 동작이라 봄.

### 3.2 시각 spec authority — tool2

Forge 가 산출하는 모든 hwp/hwpx 의 시각(폰트·여백·색상·글머리·박스 등)은
**tool2 (금감원 오피스 프로그램) 출력과 시각적으로 동등**해야 함. 시각
디테일에 의문이 생기면 권위 있는 1 차 참조는:

1. [dev-support/tool2-spec-mcp/](dev-support/tool2-spec-mcp/) — 411 메서드 + 5 템플릿 + 1623 액션 사용 cross-ref DB
2. [reference/tool2/_unpacked/한컴라이브러리_decompiled.py](reference/tool2/_unpacked/한컴라이브러리_decompiled.py) (gitignore — 개발자 로컬)

★ 입력 spec 과 시각 출력 spec 분리:
- **입력 spec** = Forge 자체 (글머리표 기반, [spec/markdown-spec.md](spec/markdown-spec.md))
- **시각 출력 spec** = tool2 (한컴라이브러리 디컴파일이 ground truth)

tool2 의 입력 directive 형식(`네모:내용` 등)은 의도적으로 미지원 — 인지
부담·LLM 친화성·markdown 도구 호환성 때문.

### 3.3 형식 정책

| 영역 | 정책 |
|---|---|
| 모든 COM 호출 | 형식 무관 (`HwpFrame.HwpObject` 가 자동 처리, 룰 코드 분기 없음) |
| 배치 모드 출력 | **.hwpx 강제** (정부 HWP 단계적 퇴출 정책 반영) |
| 실시간 모드 입력 | .hwp / .hwpx 모두 호환 |
| 실시간 모드 출력 | 입력 형식 보존 (저장은 사용자가 한/글에서) |

### 3.4 디렉토리 구조

```
forge/                          ★ 단일 src 패키지
├── __init__.py                 버전·앱명 단일 진실원본 (__version__ 등)
├── com_helpers.py              set_param() — 5 단계 COM 패턴 1 줄 헬퍼
├── hwp_session.py              ROT enum + DRM 호환 spawn + picker
├── diagnose.py                 COM attach 진단 스크립트
├── renderers/                  ★ 마크다운 요소 8 종 독립 렌더러
│   ├── base.py                 ElementRenderer 추상 베이스
│   ├── primitives.py           표·셀·폰트 공통 COM 헬퍼
│   ├── metadata.py             대제목 + 부서·일자 stamp
│   ├── section.py              Ⅰ./Ⅱ. 중제목
│   ├── subsection.py           가./나. 소제목
│   ├── bullet.py               □ ○ - · 본문 글머리
│   ├── annotation.py           * ※ † 주석
│   ├── conclusion.py           => 결론 박스
│   ├── note_callout.py         [참고] 박스
│   └── attachment.py           [붙임] 페이지 break
├── formatter/                  md → 한/글 본문 (배치 모드 진입점)
│   ├── parser.py               md → MarkdownDocument 노드 트리
│   ├── templates.py            ReportSpec / BulletStyle 등 양식 dataclass
│   └── hwpx_writer.py          dispatcher + convert_selection_to_hwpx
├── linter/                     정형 룰 (실시간 + 배치 후처리)
│   ├── _range.py               selection_range / apply_per_paragraph
│   ├── indent_align.py         들여쓰기 정렬 (bullet/annotation 라인)
│   ├── kerning.py              자간조정 (어절 잘림 방지)
│   └── squeeze.py              어절 끌어올림 (한 줄 압축)
├── ui/                         stdlib Tkinter GUI
│   ├── app.py                  메인 윈도우, hotkey 등록, picker
│   ├── hotkeys.py              Win32 RegisterHotKey manager
│   ├── hwp_picker.py           다중 인스턴스 선택 다이얼로그
│   ├── icon.py · scrolled.py · tooltip.py    UI helper
│   └── tabs/
│       ├── realtime_tab.py     탭 ① 개별 작업 (룰 + 단축키)
│       ├── settings_tab.py     탭 ② 기본정보 (양식 spec 편집)
│       └── markdown_tab.py     탭 ③ 마크다운 입력 (배치 변환)
└── rules/                      (예정) 룰셋 카탈로그 정형화 위치

run.py                          GUI 진입점 (`python run.py`)
pyproject.toml                  project 메타 + 의존성 (forge.__version__ dynamic)
.mcp.json                       dev MCP 서버 등록 (${FORGE_ROOT:-...} 환경변수)

spec/                           ★ 입력·출력 사양
├── markdown-spec.md            md 입력 spec
└── renderer-spec.md            렌더러 8 종 spec (tool2 매핑 + COM 시퀀스)

dev-support/                    개발 시점 전용 (runtime 무관)
├── hwp-api-mcp/                한컴 공식 HWP COM API 카탈로그 MCP
└── tool2-spec-mcp/             tool2 spec MCP

reference/                      gitignore — 개발자 로컬 (tool2 분해 등)
scripts/                        extract / parse / schema 유틸
tests/                          (예정)
```

### 3.5 구현 스택

| 영역 | 라이브러리 | 비고 |
|---|---|---|
| Python | 3.11+ | 환경 가변 (3.12 가 주 개발) |
| 한/글 COM | `pywin32` (`win32com.client`) | 모든 stage 공통 |
| GUI | stdlib `tkinter`/`ttk` (Windows 'vista' 테마) | 외부 의존 0 |
| md front-matter | `pyyaml` | 메타데이터 파싱 |
| 프로세스 감지 | `psutil` | 한/글 spawn 정책용 |
| dev MCP | Node.js + TypeScript | dev 전용 |

5 단계 COM 패턴은 [forge/com_helpers.py](forge/com_helpers.py) 의 `set_param()`
한 줄 헬퍼로 일괄 처리 — tool2 의 wrapper 411 개 안 만들고 한 헬퍼로 충분.

---

## 4. 입출력 사양

| 사양 | 위치 | 권위 |
|---|---|---|
| md 입력 (글머리·메타·callout 등) | [spec/markdown-spec.md](spec/markdown-spec.md) | Forge 자체 |
| 시각 렌더러 (8 종) | [spec/renderer-spec.md](spec/renderer-spec.md) | tool2 |
| 시각 디테일 (폰트·색상·여백 정확값) | tool2 디컴파일 (§3.2) | tool2 |

---

## 5. 개발 시점 도구 — MCP 우선 의무

### 5.1 두 MCP

| MCP | 답하는 질문 |
|---|---|
| **hwp-api-mcp** | "이 동작에 해당하는 한/글 액션명은? 파라미터 항목 이름은? Run 가능한가 Execute 필수인가?" |
| **tool2-spec-mcp** | "tool2 의 보고서1 대제목 박스는 어떤 액션 시퀀스를 어떤 순서로 호출하는가? 색상·여백·폰트 정확값은?" |

→ **보완 관계**: hwp-api 는 사전(辭典), tool2-spec 은 용례집. 신규 시각 요소
구현 = 둘 다 검색.

### 5.2 표준 워크플로 (5 단계)

새 시각 요소 1 개를 코드로 작성하기 전:

1. **tool2-spec-mcp 로 용례 검색** — `search_tool2_methods("배경색")` →
   유사 메서드(예: `표배경색`, `금감원페이지대제목`) 발견 →
   `get_tool2_method_source` 로 본문 확인.
2. **hwp-api-mcp 로 액션 권위 확인** — 1 단계에서 얻은 액션명을
   `search_hwp_action` 으로 검증. flag(none/plain/required/pending) 와
   ParameterSet 이름 확인.
3. **파라미터 항목 확인** — `get_hwp_parameterset` 또는 `search_hwp_member`
   로 SetItem 키 정확명 확인 (오타 포함 — 예: `BorderCorlorLeft` sic).
4. **`forge/renderers/primitives.py` 에 헬퍼 추가** (또는 기존 헬퍼 재사용).
   코드 주석에 tool2 출처(파일:라인) 명시.
5. **렌더러에서 헬퍼 호출** — primitives 만 사용. 렌더러는 액션명을 직접 알
   필요 없음.

### 5.3 금지 행위 (★★★)

1. MCP 검색 없이 액션명·파라미터명을 코드에 직접 작성. *원인*: BorderShape
   사고 — 존재하지 않는 액션을 추측으로 호출 → 런타임 NoneType 에러.
   `search_hwp_action("셀 테두리")` 1 회로 정답(`CellBorderFill`) 즉시 확인
   가능했음.
2. 디컴파일 텍스트나 PDF 를 grep 해서 MCP 우회.
3. 검색 결과를 받지 않고 추측으로 인접 액션명 만들기.

### 5.4 가용성 점검

세션 시작 시 또는 새 액션 검색 직전에 `mcp__hwp-api__*`,
`mcp__tool2-spec__*` 도구가 deferred 로 잡혔는지 확인. 안 잡혔으면:

- `.mcp.json` 의 dist/index.js 빌드 여부 (`npm run build` 각 디렉토리)
- MariaDB(`hwp_api_db`, `tool2_spec_db`) 살아있는지
- `${FORGE_ROOT}` 등 환경변수 expansion 정상인지

해결 안 되면 사용자에게 먼저 보고하고, 디컴파일 grep 우회는 **마지막 수단**.

---

## 6. AI 협업 지침

1. **이 문서가 진실 원천**. 구조·정책 변경은 코드보다 먼저 이 문서를 갱신.
2. **LLM-free 원칙 (runtime)**. 룰 실행 시점에 LLM 호출 X. LLM 은 개발 시점
   (룰 작성·디버깅) 에만 활용.
3. **tool2 spec authority**. 시각 의문 시 tool2-spec-mcp 또는 디컴파일 코드를
   먼저 확인 (§3.2). 임의로 폰트·여백·색상 정하지 말 것. tool2 코드를
   import / 차용하지는 않으나 출력 시각은 동등해야 함.
4. **MCP 우선 의무 (★★★)**. §5 워크플로 준수. 추측 금지, MCP 먼저.
5. **결정되지 않은 사항** (라이브러리 선택, 사양 세부 등)은 임의 결정 말고
   사용자 확인.
6. **작업 이력 기록**. 의미 있는 변경 시 [TASK_LOG.md](TASK_LOG.md) 에 append.
7. **문서 인덱스 갱신**. 새 spec 문서 생성 시 §7 인덱스에 추가.

---

## 7. 문서 인덱스

| 파일 | 내용 |
|---|---|
| [TASK_LOG.md](TASK_LOG.md) | 전역 작업 이력 (의미 있는 변경마다 append) |
| [README.md](README.md) | 사용자용 사용법 요약 |
| [spec/markdown-spec.md](spec/markdown-spec.md) | 개조식 md 입력 사양 |
| [spec/renderer-spec.md](spec/renderer-spec.md) | 렌더러 8 종 spec (tool2 매핑) |
| [dev-support/hwp-api-mcp/](dev-support/hwp-api-mcp/) | HWP COM API 카탈로그 MCP |
| [dev-support/tool2-spec-mcp/](dev-support/tool2-spec-mcp/) | tool2 spec MCP |
