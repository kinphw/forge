# Sentinel-Forge — AI Collaboration Guide

> 이 문서는 Claude Code, GPT Codex 등 모든 AI 협업 도구가 공유하는 프로젝트 정의서입니다.
> 개발 시 이 문서를 우선 참조하십시오.

---

## 1. 프로젝트 개요

**Sentinel-Forge**는 개조식 markdown을 입력받아 한/글 보고서(.hwpx 신규 산출 / .hwp 호환 입력)로 변환·폴리싱하는 **LLM-free HWP 툴킷**입니다.

### 자매 프로젝트와의 관계

```
[Sentinel]              [Sentinel-Forge]
LLM 보고서 작성기   →    HWP 폴리싱 도구
(환경1 전용)            (환경1·2 모두)

c:/projects/sentinel    c:/projects/sentinel-forge
        │                        ▲
        │   개조식 md (계약)      │
        └─────────────────────────┘
```

- **Sentinel** (`c:/projects/sentinel`): 법령 검토·보고서 텍스트 작성. LLM 의존, 환경1 전용. 산출물은 **개조식 md**까지
- **Sentinel-Forge** (이 프로젝트): HWP 형식화·폴리싱. LLM 무관, 환경2(폐쇄망) 운용 가능
- **결합 방식**: 두 프로젝트는 **개조식 md 사양**으로만 결합. 직접 코드 의존 없음. Sentinel을 거치지 않은 손글 md도 동일하게 처리

### 핵심 가치

- **LLM-free 운용**: 환경2(폐쇄망, LLM 접근 불가)에서도 정상 동작
- **2가지 사용 모드**: 배치(md → HWP 풀 파이프라인 STAGE 1→2→3) / 실시간(활성 한/글에 정형조작 적용 — tool2 발췌 재구현)
- **결정론적**: 동일 입력 → 동일 출력. 단위 테스트로 회귀 방지
- **tool2 시각 spec 일치**: Forge가 산출하는 모든 hwp/hwpx의 시각 결과는 금감원 공식 도구(tool2 = 금감원 오피스 프로그램)의 출력과 시각적으로 동등해야 함. tool2가 spec authority. 자세한 내용은 §5 참조.
- **반복 자동화 90% 목표**: 정형 룰로 표현 가능한 양식 작업을 흡수, 비정형은 사람이 직접 처리

### 운용 환경 매트릭스

| 환경 | LLM 접근 | 한/글 | 본 프로젝트의 역할 |
|---|:-:|:-:|---|
| 환경1 (개발 PC) | ○ | ○ | 룰 작성·디버깅(LLM·MCP 도움) + 모든 모드 실행 |
| 환경2 (폐쇄망 PC) | × | ○ | 사전 작성된 룰 실행만 (배치/실시간) |

---

## 2. 핵심 설계 원칙: 3단계 파이프라인

> **사용자 관점 vs 내부 구조의 차이**
> 사용자에겐 단 2가지 기능만 보입니다 — (1) md → hwpx 완성본, (2) 활성 문서 polishing.
> 내부적으로는 (1)이 STAGE 1→2→3을 통째 실행하고, (2)는 STAGE 3 룰만 골라 호출합니다.
> 3단계 분리는 **관심사 분리·테스트 가능성·코드 재사용**을 위한 내부 설계이지,
> 사용자에게 노출되는 인터페이스가 아닙니다. STAGE 1·2를 단독 호출하는 모드는 제공하지 않습니다.

```
입력: 개조식 md
        │
        ▼
┌──────────────────────────────────┐
│  STAGE 1 — Formatter             │  산출: .hwpx 초안
│  md → HWPX(ZIP+XML) 변환         │  한/글 ✗, LLM ✗
│  결정론적, 폐쇄망 OK             │
└──────────────┬───────────────────┘
               ▼
┌──────────────────────────────────┐
│  STAGE 2 — Linter                │  산출: .hwpx 정규화본
│  HWPX → HWPX (XML 룰 적용)       │  한/글 ✗, LLM ✗
│  표준 양식 강제·자동 교정         │
│  - 번호체계 깊이                  │
│  - 폰트·크기 일관성               │
│  - 표 스타일 통일                 │
│  - 메타데이터 삽입                │
│  - 들여쓰기 정합성                │
└──────────────┬───────────────────┘
               ▼
┌──────────────────────────────────┐
│  STAGE 3 — Polisher              │  산출: .hwpx 최종 (배치 모드 기본)
│  COM API (형식 무관)              │  한/글 ○, LLM ✗
│  렌더링-aware 작업만 수행         │
│  - 자간 조정 (어절 줄바꿈 방지)  │
│  - 페이지 break 최적화           │
│  - 표 열 너비 auto-fit            │
│  - 목차 페이지번호 갱신           │
└──────────────────────────────────┘
출력: 배치 모드 → .hwpx / 실시간 모드 → 입력 형식 보존(.hwp 또는 .hwpx)
```

### 단계별 요약

| 단계 | 입력 | 산출 | 한/글 | LLM(runtime) |
|---|---|---|:-:|:-:|
| **1** Formatter | 개조식 md | .hwpx 초안 | × | × |
| **2** Linter | .hwpx 초안 | .hwpx 정규화본 | × | × |
| **3** Polisher | .hwpx (또는 실시간 모드의 .hwp/.hwpx) | 배치 모드: .hwpx / 실시간 모드: 입력 형식 보존 | ○ | × |

### 두 시점의 분리 (중요)

| 시점 | LLM | MCP | 의미 |
|---|:-:|:-:|---|
| **개발 시점** (룰 작성) | ○ | ○ | 환경1에서 개발자(또는 Sentinel 에이전트)가 hwp-api-mcp로 API를 검색하며 새 룰을 작성. 결과는 결정론적 코드 |
| **실행 시점** (룰 적용) | × | × | 환경1·2 어디서든 미리 작성된 코드를 그대로 실행. LLM·MCP 불필요 |

→ **MCP는 본 프로젝트의 dev-support 도구**이며, 실행 의존성이 아닙니다.

### 형식 정책 (HWP / HWPX) — 2026-04-26 결정

정부의 HWP 단계적 퇴출 의사결정을 반영한 형식 정책. 핵심: **신규 산출물은 HWPX, 레거시 .hwp 입력은 그대로 호환**.

| 영역 | 정책 | 비고 |
|---|---|---|
| STAGE 1·2 (XML 처리) | **HWPX 전용** | HWPX = ZIP+XML, 바이너리 .hwp 파서/생성기 영원히 불필요 |
| STAGE 3 (COM 호출) | **형식 무관** | `HwpFrame.HwpObject` COM API 가 형식 자동 처리. 룰 코드는 단 한 줄도 분기 없음 |
| 배치 모드 출력 | **.hwpx 강제** | 신규 문서는 정책 부합 형식으로만 |
| 실시간 모드 입력 | .hwp / .hwpx 모두 | 사용자가 가진 레거시 .hwp 그대로 받음 |
| 실시간 모드 출력 | 입력 형식 보존 (기본) | 별도 "hwpx로 변환 저장" 옵션 제공 가능 |

→ 실용적 의미: Forge 가 .hwp 파일을 거부할 일은 없음. STAGE 3 룰은 형식 분기 없이 작동. STAGE 1·2 코드 베이스는 .hwp 바이너리에 대해 무지(無知).

---

## 3. 사용 모드

본 프로젝트는 **2가지 사용 모드**를 제공한다. 두 모드는 독립적으로도, 순차적으로도 사용할 수 있다.

| 사용자 관점 모드 | 내부 stage 호출 |
|---|---|
| 1) 배치 — md → hwpx 완성본 | STAGE 1 → STAGE 2 → STAGE 3 (전체) |
| 2) 실시간 — 활성 문서 polishing | STAGE 3 룰 중 사용자 선택분 |

→ STAGE 1·2 단독 호출 모드(예: lint-only)는 제공하지 않음. 사용자 단순성 우선.

### 1) 배치 모드 — md → hwpx 보고서 초안 생성

```bash
sentinel-forge build report.md --output report.hwpx
```

개조식 md를 입력받아 **STAGE 1→2→3을 통째 실행**해 hwpx 보고서 초안을 만든다. 입력 md의 출처는 두 가지:
- **Sentinel 산출 md** (가장 일반적): Sentinel STAGE 2가 LLM으로 작성한 개조식 md
- **사용자 손글 md**: 사용자가 [spec/markdown-spec.md](spec/markdown-spec.md) v1.1 사양에 따라 직접 타이핑

두 경우의 처리는 동일하다. STAGE 3까지 자동화로 종결하면 그대로 보고서 완성, 후속으로 모드 2를 추가 적용해도 됨.

### 2) 실시간 모드 — 활성 hwp/hwpx 정형조작 (tool2 발췌 재구현)

```bash
sentinel-forge polish-active --rule adjust_spacing
sentinel-forge polish-active --rule prevent_orphan_title --section 2
sentinel-forge polish-active --rule reset_page_number
```

이미 한/글에 열려 있는 hwp/hwpx에 **사용자가 버튼/명령으로 골라 정형 룰을 적용**한다. 사실상 tool2(금감원 오피스 프로그램) 와 같은 기능이며, **tool2의 100+ 기능 전체를 옮기지 않고 Forge가 필요한 핵심만 발췌해서 재구현**한다는 점이 다르다.

각 룰은 STAGE 3 폴리셔의 일부로, COM API를 통해 활성 hwp 인스턴스에 즉시 적용된다.

### 두 모드 조합 사용 예

```text
[모드 1 단독]    Sentinel md → Forge build → 완성 .hwpx
[모드 1 → 모드 2] Sentinel md → Forge build → .hwpx 를 한/글에서 열어 실시간 정형조작 추가
[모드 2 단독]     기존 .hwp/.hwpx → 한/글에서 열고 실시간 룰 호출 → 입력 형식 보존 저장
```

→ 어느 시나리오든 **출력 hwp(x) 의 시각은 tool2 spec과 시각적으로 동등** (§5 참조). 형식 정책은 §2 "형식 정책" 표 참조.

### 구현 형태 — Full GUI (단일 진입점) — 2026-04-26 결정

**Forge 는 GUI 단일 진입점**으로 구현한다 (CLI 미제공). 이유:
- tool2 와 같은 사용감 (사용자가 이미 익숙)
- 두 모드 모두 GUI 한 곳에서 완결 — 사용자가 명령행을 외우거나 인자를 기억할 필요 없음
- 활성 한/글 프로세스 인식·연결이 GUI 워크플로우에 자연스러움
- Sentinel 파이프라인 통합 시점에서 CLI 가 필요해지면 그때 추가 (지금은 YAGNI)

**진입 동작**:
1. 실행 시 독립 윈도우 1개 생성 (Tkinter + ttkbootstrap 추정)
2. 시스템에 이미 실행 중인 한/글 프로세스를 자동 감지 → COM 인스턴스 attach. 없으면 신규 생성.
3. 사용자는 윈도우 안에서 모드 1·2 모두 수행

**탭 구조** (3 tabs):

| 탭 | 용도 | 주요 stage |
|---|---|---|
| 1) **기본정보** | 보고서명·작성부서·작성일 등 메타데이터 입력. 템플릿 선택 (보고서1·원페이지·…). 출력 경로 지정 | 전 모드 공통 입력 |
| 2) **마크다운 입력** | 좌측 텍스트 에디터에 개조식 md 입력 (또는 파일 불러오기). "변환" 버튼 클릭 시 STAGE 1→2→3 통째 실행. 우측에 진행 로그·미리보기 | 모드 1 (배치) |
| 3) **개별 작업** | tool2 패널처럼 룰 버튼들 (자간조정·페이지 break 정리·쪽번호 초기화·표 여백 0 등). 클릭 시 활성 hwp 에 즉시 적용 | 모드 2 (실시간) |

**내부 구조**: GUI 는 얇은 껍데기 — 핵심 로직은 `forge/` 코어 라이브러리에. 탭들은 forge.* 모듈의 함수를 호출만 함. 향후 CLI 추가 시 같은 코어 사용.

---

## 4. md 입력 사양 (Sentinel과의 계약)

자세한 사양은 [spec/markdown-spec.md](spec/markdown-spec.md) (**v1.3 확정**).
**이 사양 파일은 Sentinel 측 사본(`c:/projects/sentinel/docs/markdown-spec.md`)과 항상 동일해야 합니다.** 변경 시 양 위치 동시 갱신 필수.

**핵심 원칙**: 마크다운은 **논리 구조**(메타데이터·층위·요약단어·주석·강조·결론 화살표·callout 7종)만 표기. 시각적 서식은 모두 Forge 책임.

요약:
- **메타데이터**: YAML front-matter (보고서명·작성부서·작성일)
- **층위**: 6단계 — `1.`/`2.` (섹션) → `가.`/`나.` (소제목) → `□` → `○` → `-` → `·`. 라인 시작 글머리 문자만으로 식별 (들여쓰기 자유). 소제목은 사용 자유
- **요약단어**: `□` 레벨에 `(요약) 본문` 형식 의무 (섹션·소제목은 자체로 요약 역할이라 불필요)
- **주석**: 본문 `*` 참조 → `* 설명` / 일반 주석 → `※ 설명`. 한 단락 공존 가능
- **강조 (Bold)**: `__X__` (언더스코어 쌍, 조사·어미 제외). 이탤릭 미사용. asterisk는 참조 전용
- **결론 화살표**: 라인 시작 `=>` (위치 자유)
- **Callout 박스**: `[참고]` (본문 중간 보충 박스) / `[붙임]` `[붙임 N]` (별도 페이지 첨부). 라인 시작 단독, 다음 빈 줄까지 본문

상세 규범·예시는 [spec/markdown-spec.md](spec/markdown-spec.md) 참조.

### tool2 directive (`네모:내용` 등) 미지원 — 의사결정 (2026-04-26)

tool2(금감원 오피스 프로그램)는 자체 directive 기반 마크다운(`네모:내용`, `당구장:내용`, `소제목:내용`, `표:열1:열2`)을 보유한다. Forge는 **이 형식을 입력으로 받지 않는다**. 결정 근거:

1. **인지 부담**: Forge 글머리표(`□ ○ - ·`)는 산출물 시각과 1:1 대응이라 작성 속도가 빠름. tool2 directive는 "큰항목→네모" 같은 의식적 변환 단계 필요.
2. **LLM 친화성**: Sentinel(LLM)이 md를 생성하는데, 글머리표 markdown은 학습 분포가 풍부하지만 한글 keyword directive는 거의 없음. Sentinel 출력 품질 직결.
3. **호환성**: 글머리표 spec은 일반 markdown 도구(lint·preview·diff)와 호환. tool2 directive는 비표준이라 모든 tooling 자체 구현 필요.
4. **이미 확정된 계약**: spec v1.1 + Sentinel STAGE 2 v1.1 반영 완료. 변경 시 양 프로젝트 재배포.
5. **차용 가치 부재**: tool2 directive의 강점(자동 카운팅/표 inference/특수 토큰)은 Forge spec에 이미 동등 또는 더 단순한 수단(`1.` 직접 표기 / GFM 표 / front-matter `작성일`)으로 대응됨.

→ **입력 spec = Forge 글머리표 spec / 시각 출력 spec = tool2 권위** (§5 참조).

---

## 5. 시각적 양식 룰 (Forge 자동 적용) + tool2 spec authority

### 5.1 tool2 spec authority (★ 중심 원칙)

**tool2 = 금감원 오피스 프로그램** = 금감원 공식 도구로서 **금감원 보고서 시각의 spec authority**. Forge는 tool2 코드를 import하거나 차용하지 않지만, **출력 hwp/hwpx의 시각 결과는 tool2 출력과 시각적으로 동등해야 한다**.

| 항목 | 입력 spec | 시각 출력 spec |
|---|---|---|
| 권위 | Forge 자체 ([spec/markdown-spec.md](spec/markdown-spec.md) v1.1) | **tool2 (금감원 공식)** |
| 의미 | "어떤 의미를 표기하는가" | "어떤 시각으로 표기하는가" |
| 의문 시 참조 | 본 문서 + spec 파일 | [reference/tool2/_unpacked/한컴라이브러리_decompiled.py](reference/tool2/_unpacked/한컴라이브러리_decompiled.py) |
| 룰 작성 도구 | (직접 작성) | **tool2-spec MCP** + hwp-api MCP |

**적용 예**: Forge md `□ 큰내용` → STAGE 2 Linter는 "큰내용"을 맑은 고딕 15pt + Bold + 내어쓰기 -22pt로 출력해야 함 (=금감원페이지 표준). 이 수치들은 tool2 코드에서 추출 (분석노트 §12.5 / `금감원페이지` 메서드 본문).

### 5.2 자동 적용되는 시각 룰 카탈로그

마크다운 사양에 표기된 의미를 받아 다음 시각적 양식을 자동 적용한다 (구체 룰은 `rules/linter/` STAGE 2 및 `rules/polisher/` STAGE 3에 코드로 존재).

| 자동 적용 대상 | 단계 |
|---|---|
| 메타데이터(보고서명) → 노란 박스 대제목 (HY헤드라인M 17pt) | STAGE 2 |
| 메타데이터(부서·일자) → 우정렬 stamp (휴먼명조 12pt) | STAGE 2 |
| 섹션 `1.`/`2.` → 중제목 (Ⅰ./Ⅱ. + HY견명조/HY헤드라인M, 파란 밑줄) | STAGE 2 |
| 소제목 `가.`/`나.` → 라벤더 박스 + 진파랑 테두리 (HY헤드라인M 15pt) | STAGE 2 |
| 요약단어 `(...)` → `□` 라인의 첫 단어를 맑은 고딕 15pt Bold | STAGE 2 |
| 층위(`□` `○` `-` `·`) → tool2 표준 들여쓰기·글머리·폰트 매핑 | STAGE 2 |
| 주석(`*` 참조 / `※` 일반) → 별도 폰트·크기 적용 | STAGE 2 |
| `[참고]` callout → 파란 헤더 + 흰 글씨 박스 (HY헤드라인M 15pt) | STAGE 2 |
| `[붙임]` / `[붙임 N]` → 자동 페이지 break + 별지 첨부 | STAGE 2 |
| 결론 화살표(`=>`) → 민트 점선 박스 ⇨ (휴먼명조 15pt) | STAGE 2 |
| Bold 변환 (`__X__` → 굵게 처리) | STAGE 2 |
| 자간 조정 (어절 줄바꿈 방지) | STAGE 3 |
| 분량 2페이지 이내 강제 (초과분 `[붙임]` 자동 분리) | STAGE 3 |
| 페이지 break 최적화 (고아 제목 방지) | STAGE 3 |
| 목차 페이지번호 갱신 | STAGE 3 |

상세 편집 규칙은 [spec/editing-rules.md](spec/editing-rules.md) (작성 예정 — tool2 분해 결과를 토대로 v0.1 작성 예정).

---

## 6. 디렉토리 구조 (예정)

```
sentinel-forge/
├── CLAUDE.md                  ← 이 파일
├── README.md                  ← 사용자용 사용법 + md 사양 요약
├── TASK_LOG.md                ← 전역 작업 이력
├── .mcp.json                  ← hwp-api + tool2-spec MCP 등록 (hdbuser 자격 포함)
├── .gitignore
├── pyproject.toml             ← (예정) Python 프로젝트 정의 + 의존성
│
├── spec/
│   ├── markdown-spec.md       ← 개조식 md 입력 사양 (Sentinel과의 계약, v1.3)
│   └── editing-rules.md       ← HWP 편집 규칙 정형화 (작성 예정, tool2 분해 기반)
│
├── forge/                     ← ★ 핵심 코어 라이브러리 (UI 없음)
│   ├── __init__.py
│   ├── com_helpers.py         ← set_param() 등 5단계 패턴 헬퍼
│   ├── stage_1_formatter/     ← STAGE 1: md → HWPX (lxml + zipfile)
│   ├── stage_2_linter/        ← STAGE 2: HWPX → HWPX (XML 룰)
│   ├── stage_3_polisher/      ← STAGE 3: COM 룰 (형식 무관, 산출은 .hwpx 기본)
│   ├── rules/
│   │   ├── linter/            ← STAGE 2용 XML 룰 (결정론적)
│   │   └── polisher/          ← STAGE 3용 COM 룰 (결정론적)
│   ├── hwp_session.py         ← 활성 한/글 프로세스 attach + 신규 생성 헬퍼
│   └── utils/                 ← 한글화·번호 변환 등 순수 함수 유틸
│
├── ui/                        ← ★ Tkinter GUI (단일 진입점)
│   ├── __init__.py
│   ├── app.py                 ← 메인 윈도우, 한/글 프로세스 자동 감지·attach
│   ├── tabs/
│   │   ├── settings_tab.py    ← 탭 1: 기본정보 (메타데이터·템플릿 선택)
│   │   ├── markdown_tab.py    ← 탭 2: 마크다운 입력 + STAGE 1→2→3 실행
│   │   └── realtime_tab.py    ← 탭 3: 개별 작업 버튼 (STAGE 3 룰 수동)
│   └── widgets/               ← 공통 위젯 (md 에디터·로그 패널 등)
│
├── reference/
│   ├── official_pdfs/         ← 한컴 공식 PDF 4종 + 추출 txt
│   ├── hwp_api.sqlite         ← MariaDB hwp_api_db에서 export된 사본 (F5)
│   ├── tool1/                 ← FSS IT트렌드 #7 (Python+Tkinter, hwp_auto.py)
│   ├── tool2/                 ← 금감원 오피스 프로그램 (분석 자료)
│   │   ├── 분석노트.txt        ← tool2 완전 분해 분석 (★ 1차 참조)
│   │   ├── 보고서1_spec.md     ← 금감원페이지 정확 spec
│   │   └── _unpacked/         ← 디컴파일 산출물 (일부만 commit)
│   │       └── 한컴라이브러리_decompiled.py  (411 메서드, 16,656줄)
│   └── _tools/                ← gitignore — pycdc 빌드 클론, 사용자 로컬에서 재구성
│
├── dev-support/
│   ├── hwp-api-mcp/           ← HWP COM API 카탈로그 MCP (개발 시점 전용)
│   └── tool2-spec-mcp/        ← tool2 spec MCP (개발 시점 전용)
│
├── scripts/                   ← extract-pdf, parse, schema, export-sqlite 등
└── tests/
    ├── stage_1/
    ├── stage_2/
    ├── stage_3/
    ├── ui/                    ← UI 통합 테스트
    └── samples/               ← md / hwpx fixture
```

### 구현 언어 — Python 단일 스택 확정 (2026-04-26)

| 영역 | 라이브러리 | 비고 |
|---|---|---|
| Python 버전 | **3.12** (현재 환경) | tool2 의 3.10 과는 별개 |
| STAGE 1·2 (HWPX XML) | `lxml` + `zipfile` (표준) | HWPX = ZIP+XML, 직접 작성 |
| STAGE 3 (COM) | `pywin32` 의 `win32com.client` | pyhwpx 미도입 (lock-in 회피, §11.5 협업 지침) |
| GUI | `tkinter` + **`ttkbootstrap`** | 모던 테마, Tkinter 호환 |
| 마크다운 파서 | `markdown-it-py` 또는 자체 정규식 | spec 단순해서 자체 가능 |
| 5단계 COM 패턴 | `forge/com_helpers.py` 의 `set_param()` 1줄 헬퍼 | wrapper 메서드는 만들지 않음 |
| dev-support MCP | Node.js + TypeScript (기존) | 변경 없음, dev 전용 |

→ Forge 본체는 **Python 3.12 단일 스택**. dev-support MCP 만 Node 유지.

---

## 7. Sentinel에서 이관할 자산

### 이관 완료 자산 (2026-04-25)

| 자산 | 이관 후 위치 | 비고 |
|---|---|---|
| HWP API 공식 PDF + 추출 txt | [reference/official_pdfs/](reference/official_pdfs/) | 한컴 제공 4종 + PyMuPDF 추출 txt |
| `hwp-api-mcp` MCP 서버 | [dev-support/hwp-api-mcp/](dev-support/hwp-api-mcp/) | 룰 작성용 dev 도구. 환경1 전용 |
| 추출·적재 스크립트 4종 | [scripts/](scripts/) | extract-hwp-api / parse / schema / smoke_test (경로 참조 갱신 완료) |
| MCP 등록 | [.mcp.json](.mcp.json) | Sentinel `.mcp.json`에서 제거 후 본 프로젝트에 신규 등록 |
| (placeholder) | `reference/tool1/`, `reference/tool2/` | Sentinel에서 함께 보존된 빈 폴더 |

### 이관 작업 체크리스트 (F0)

- [x] 4종 자산 물리 이동
- [x] Sentinel `.mcp.json`에서 `hwp-api` 항목 제거 + Forge `.mcp.json` 신규 생성
- [x] 이동된 스크립트의 상대경로 갱신 (`reference/official_pdfs`, `dev-support/hwp-api-mcp`)
- [x] hwp-api-mcp 새 위치에서 재빌드 + smoke test 6종 통과
- [x] Sentinel CLAUDE.md 디렉토리 트리에서 hwp 관련 항목 제거
- [ ] `hwp_api_db` (MariaDB) → `reference/hwp_api.sqlite` export — F5(환경2 배포) 시 진행
- [ ] dev-support/hwp-api-mcp의 DB 클라이언트를 SQLite 또는 듀얼 지원으로 갱신 — 위와 동시
- [ ] Sentinel의 MariaDB `hdbuser` 정리 여부 결정 — 개발 편의상 유지 (현재 hwp-api-mcp가 환경1에서 이 계정으로 hwp_api_db 조회)

**현재 상태**: 환경1에서는 MariaDB `hwp_api_db` + `hwp-api-mcp`가 정상 동작. 환경2 배포 전에 SQLite export 작업 필요.

### tool2 분해 + tool2-spec-mcp (2026-04-26)

| 자산 | 위치 | 비고 |
|---|---|---|
| tool2 원본 (exe + PDF) | [reference/tool2/](reference/tool2/) | 금감원 공식 도구 |
| 분해 분석 노트 | [reference/tool2/분석노트.txt](reference/tool2/분석노트.txt) | ★ 1차 참조 (§12 최종 결론) |
| 디컴파일 산출 | [reference/tool2/_unpacked/한컴라이브러리_decompiled.py](reference/tool2/_unpacked/한컴라이브러리_decompiled.py) | 411 메서드 100% |
| 패치된 pycdc | [reference/_tools/pycdc/](reference/_tools/pycdc/) | 4개 패치 적용. data.cpp의 `std::exit(1)` 제거가 결정적 |
| MariaDB tool2_spec_db | (DB) | hdbuser, 7 테이블, 411 메서드 + 5 템플릿 + 1623 액션 사용 cross-ref |
| **tool2-spec-mcp** | [dev-support/tool2-spec-mcp/](dev-support/tool2-spec-mcp/) | 7 도구 노출, .mcp.json 등록 완료 |

**현재 상태**: 환경1에서 tool2-spec-mcp 정상 동작. 룰 작성 시 `search_tool2_methods` / `get_tool2_template` / `get_tool2_method_source` 등으로 tool2 spec을 즉시 조회 가능. 환경2 배포에 포함되지 않음 (개발 시점 전용).

---

## 8. 개발 우선순위 (로드맵)

### F0 — 자산 이관 ✅ 완료 (2026-04-25)

§ 7 이관 체크리스트 참조. SQLite export 작업만 F5(환경2 배포) 시점으로 미뤄둠.

### F1 — md 입력 사양 확정 ✅ 완료 (2026-04-25)

- [x] 개조식 markdown 사양 명문화 ([spec/markdown-spec.md](spec/markdown-spec.md) v1.1)
- [x] Sentinel STAGE 2 시스템 프롬프트 v1.1 반영
- [x] 양 프로젝트 사본 동기화 (Forge `spec/` ↔ Sentinel `docs/`)
- [x] tool2 directive 미지원 결정 (2026-04-26, §4 참조)

### F1.5 — tool2 분해 + spec MCP ✅ 완료 (2026-04-26)

금감원 공식 도구(tool2 = 금감원 오피스 프로그램)의 spec authority 확보.

- [x] PyInstaller exe 추출 (pyinstxtractor-ng) — 1344 파일 + PYZ 159 모듈
- [x] pycdc 빌드 + 4개 패치 (data.cpp `std::exit(1)` 제거가 결정적)
- [x] 411 메서드 100% Python 디컴파일 (`한컴라이브러리_decompiled.py` 16,656줄)
- [x] 분석노트 §1~§12 작성 ([reference/tool2/분석노트.txt](reference/tool2/분석노트.txt))
- [x] MariaDB `tool2_spec_db` 스키마 + seed 적재 (411 메서드, 5 템플릿, 1623 액션 사용)
- [x] [dev-support/tool2-spec-mcp/](dev-support/tool2-spec-mcp/) 7 도구 구현 + 빌드 + smoke test
- [x] [.mcp.json](.mcp.json) 등록
- [ ] bullet_specs 수동 큐레이션 (5 템플릿 × 4 층위 ~20행) — 후속

### F2 — STAGE 1: HWPX Formatter

- HWPX 명세(KS X 6101) 핵심 구조 조사 — content.hpf / sections / styles / numbering
- 기존 HWPX 라이브러리 평가 (Python: pyhwpx, hwp5tools 등 / Node 미성숙 여부)
- 구현 언어 결정 (사용자 합의)
- md → HWPX 변환기 PoC + 단위 테스트
- 기본 양식 적용 (폰트·여백·헤더 - editing-rules.md 기반)

### F3 — STAGE 2: HWPX Linter

- 린터 룰셋 카탈로그 정의 (편집 규칙 → 룰 형식화)
- XML 변환 모듈 (lxml 기반 추정)
- 룰 추가 인터페이스 (피드백을 룰로 흡수하는 구조)
- 회귀 테스트 프레임 (룰 추가 시 기존 케이스 깨지지 않음 보장)

### F4 — STAGE 3: COM Polisher

- HWP COM 어댑터 (pywin32 추정)
- 자간 조정 룰 — 사용자가 명시한 핵심 기술 병목
- 페이지 break 룰 — 고아 제목 방지, 표 분리 방지
- 목차 페이지번호 갱신
- 실시간 모드 — `polish-active` CLI로 열린 한/글 인스턴스에 STAGE 3 룰 수동 호출

### F5 — 환경2 배포

- SQLite reference 패키징
- 단독 실행 배포본 (PyInstaller 또는 동등 도구)
- 사용자 매뉴얼·튜토리얼

### F6 — OCR 검증 (선택)

출력 이미지 분석으로 서식 이상 탐지. 후순위.

---

## 9. 진행상황 체크

### 현재 상태 요약

- 현재 단계: **F0·F1·F1.5 완료. F2(STAGE 1 Formatter) 착수 직전**
- F0(자산 이관) 완료: HWP 관련 4종 자산 이관 + hwp-api-mcp 검증 완료
- F1(md 사양) 완료: spec v1.1 확정 + Sentinel STAGE 2 v1.1 반영 + tool2 directive 미지원 결정 (2026-04-26)
- F1.5(tool2 분해) 완료 (2026-04-26): tool2 411 메서드 100% 디컴파일 + tool2-spec-mcp 운영. tool2가 시각 spec authority 임을 명시.
- 다음 우선순위: **F2 — STAGE 1 HWPX Formatter 착수**. HWPX 명세(KS X 6101) 조사부터.
- 최신 갱신일: **2026-04-26**

### Phase 상태판

| Phase | 상태 | 비고 |
|---|---|---|
| **F0** 자산 이관 | **완료 (2026-04-25)** | 4종 자산 이동 + .mcp.json 재등록 + 새 위치에서 smoke test 통과. SQLite export는 F5에서 |
| **F1** md 사양 확정 | **완료 (2026-04-25)** | spec/markdown-spec.md v1.1 + Sentinel STAGE 2 프롬프트 반영 + tool2 directive 미지원 결정 |
| **F1.5** tool2 분해 + spec MCP | **완료 (2026-04-26)** | 411 메서드 디컴파일 + tool2-spec-mcp 운영. spec/editing-rules.md 작성의 ground truth 확보 |
| **F2** STAGE 1 Formatter | **착수 직전** | HWPX 명세 조사부터 |
| **F3** STAGE 2 Linter | 대기 | F2 완료 후. tool2-spec-mcp 활용해 룰 작성 |
| **F4** STAGE 3 Polisher | 대기 | F2 완료 후 (병렬 가능). tool2-spec-mcp + hwp-api-mcp 활용 |
| **F5** 환경2 배포 | 대기 | F2~F4 완료 후 |
| **F6** OCR 검증 | 후순위 | |

### 작업 인계 규칙

1. 새 작업 전 `현재 상태 요약`과 Phase 상태판 확인.
2. 의미 있는 변경 후 Phase 상태판 갱신 + `TASK_LOG.md` append.
3. 새 문서 생성 시 § 10 문서 인덱스 갱신.
4. md 사양 변경은 **반드시 Sentinel과 동시 합의** 후 진행.

---

## 10. 문서 인덱스

| 파일 | 상태 | 내용 |
|---|---|---|
| TASK_LOG.md | **작성완료** | 전역 작업 이력 |
| [spec/markdown-spec.md](spec/markdown-spec.md) | **확정 (v1.1)** | md 입력 사양 (계약). 6종 의미(메타데이터·층위·요약단어·주석·강조·결론 화살표). **`c:/projects/sentinel/docs/markdown-spec.md`와 동기 유지 필수** |
| spec/editing-rules.md | 미작성 (예정) | HWP 편집 규칙 정형화. tool2 분석노트 §12.5 + tool2-spec-mcp 데이터를 토대로 작성 |
| [reference/tool2/분석노트.txt](reference/tool2/분석노트.txt) | **확정** | tool2 완전 분해 분석. §12에 최종 결론. spec authority 데이터 포함 |
| reference/README.md | 미작성 | 레퍼런스(SQLite·PDF·tool2) 사용법 |
| [dev-support/hwp-api-mcp/](dev-support/hwp-api-mcp/) | 운영 중 | HWP COM API 카탈로그 MCP (Sentinel에서 이관) |
| [dev-support/tool2-spec-mcp/](dev-support/tool2-spec-mcp/) | 운영 중 (2026-04-26) | tool2 spec MCP. 7 도구 노출 |

---

## 11. AI 협업 지침

이 문서를 읽는 AI(Claude, GPT 등)는 다음을 준수하십시오.

1. **이 문서를 진실의 원천으로 취급.** 구조·우선순위 변경 시 이 문서를 먼저 업데이트.
2. **LLM-free 원칙 유지 (runtime).** STAGE 1·2·3 **실행 시점**에는 LLM 호출이 없어야 합니다. LLM은 **개발 시점**(룰 작성)에만 활용.
3. **Sentinel과의 결합 금지.** Sentinel을 import하거나 Sentinel DB(`sentinel_db`, `fss_document_db`)에 접근하지 않습니다. 결합 지점은 **md 파일 하나뿐**.
4. **md 사양 동기화.** [spec/markdown-spec.md](spec/markdown-spec.md)는 Sentinel의 동일 파일(`c:/projects/sentinel/docs/markdown-spec.md`)과 항상 일치해야 합니다. 한쪽을 수정하면 반드시 다른 쪽도 동일하게 갱신하고, 사양 문서의 변경 이력 표에 기록하십시오. 사양 변경은 양 프로젝트의 합의로만 진행합니다. Sentinel 출력 변화에 맞춰 사양을 임의 수정하지 마십시오.
5. **HWP 의존성 격리.** STAGE 3만 Windows + 한/글 의존. STAGE 1·2는 OS·HWP 무관. 의존성 누설 금지.
6. **환경2 운용성 우선.** 새 기능 추가 시 환경2(폐쇄망, LLM 없음)에서 동작 가능한지 항상 검토. SQLite 가능한데 MariaDB 강요 금지 등.
7. **결정되지 않은 사항** (구현 언어, 라이브러리 선택, 사양 세부 등)은 임의 결정하지 말고 사용자에게 확인.
8. **작업 이력 기록.** 의미 있는 변경 시 `TASK_LOG.md`에 append.
9. **★ tool2 spec authority 준수.** Forge가 산출하는 모든 hwp/hwpx의 시각 spec 의문 시 **tool2-spec-mcp** 또는 [reference/tool2/_unpacked/한컴라이브러리_decompiled.py](reference/tool2/_unpacked/한컴라이브러리_decompiled.py) 가 권위 있는 참조. 임의로 폰트·여백·글머리 속성을 정하지 말고 tool2 코드/spec을 먼저 확인할 것. tool2 코드를 import/차용하지는 않으나 출력 시각은 동등해야 함.
10. **★ 입력 spec / 출력 spec 분리 인식.** Forge 입력 마크다운 spec = Forge 자체 (글머리표 기반). Forge 출력 시각 spec = tool2. tool2의 입력 형식(directive 기반 `네모:내용` 등)은 의도적으로 미지원 (§4 참조). 두 spec을 혼동하지 말 것.
11. **MCP 활용 권장 (개발 시점).** 룰 작성 시:
    - HWP COM API 검색 → `hwp-api-mcp` (search_hwp_action / get_hwp_member 등)
    - tool2 spec 조회 → `tool2-spec-mcp` (search_tool2_methods / get_tool2_template / get_tool2_method_source 등)
    두 MCP 모두 [.mcp.json](.mcp.json) 등록 완료.

---

*최초 작성: 2026-04-25 (Sentinel CLAUDE.md에서 STAGE 3·4·5와 HWP 자산을 분리하여 신설)*
*최신 갱신: 2026-04-26 — F1.5(tool2 분해 + tool2-spec-mcp) 완료 반영 + 사용 모드 명확화 + tool2 spec authority 원칙 명문화*
*2026-04-26 추가 갱신 — 형식 정책 (§2 "형식 정책") 신설: 정부 HWP 퇴출 정책 반영. 신규 산출=HWPX, STAGE 3 COM은 형식 무관, 레거시 .hwp 입력 호환 유지*
*2026-04-26 추가 갱신 — §2 "사용자 관점 vs 내부 구조" 도입부 + §3 모드↔stage 매핑표 추가. 사용자 노출 = 2 모드만, STAGE 1·2 단독 호출 모드(lint-only) 명시적 미제공*
*2026-04-26 추가 갱신 — markdown spec v1.3: 소제목(`가.`/`나.`) + Callout 박스(`[참고]`/`[붙임]`) 신설. tool2 금감원페이지의 모든 시각 요소를 md spec으로 표현 가능. CLAUDE.md §4·§5 동기 갱신 + Sentinel 측 사본도 sync*
*2026-04-26 추가 갱신 — UI 아키텍처 = **Full GUI 단일 진입점** 확정 (CLI 미제공). Tkinter + ttkbootstrap, 3-tab 구조 (기본정보·마크다운입력·개별작업). 활성 한/글 프로세스 자동 감지·attach. §3 구현 형태 신설 + §6 디렉토리 트리 갱신 + 구현 언어 Python 3.12 단일 스택 확정. 본격 개발 개시 — git init 수행 + .gitignore 전면 갱신*
