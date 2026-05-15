# forge

## Ver

0.2.0 (260428)

## 기능

1. 개조식 markdown을 한/글 보고서(.hwpx 신규 산출 / .hwp 호환 입력)로 변환
2. 실시간 한/글 COM API 조작으로 문서 편집 효율화 (자간조정·들여쓰기 정렬·
   선택 영역 → 마크다운 변환 등)

## 사용 모드 (2가지)

사용자 관점에서 Forge 는 두 가지 동선만 제공한다. GUI 한 윈도우(`python run.py`)
에서 모두 처리.

- **배치 모드 — md → 새 .hwpx**
  - 탭 ③ "마크다운 입력" 에 개조식 md 를 입력 → "변환" 클릭
  - 출력은 항상 .hwpx (정부 HWP 단계적 퇴출 정책 반영)

- **실시간 모드 — 활성 문서 정형 조작**
  - 탭 ① "개별 작업" 에서 룰 버튼 클릭, 또는 시스템 전역 단축키
    (Ctrl+Shift+Q/W/A/S/D/Z/X) 호출
  - 한/글에 이미 열려 있는 .hwp/.hwpx 에 룰 1 개씩 적용
  - 출력은 입력 형식 보존 (저장은 사용자가 한/글에서)

[사용자 키] → OS → [hotkeys.py 메시지 펌프] → [app.py: ACTIONS 등록] → [actions.ACTIONS.invoke] → [realtime_tab._run_*] → [linter/*.py 알고리즘] → [한/글 COM]


## 형식 정책 (HWP / HWPX)

- **신규 산출물 = .hwpx** (배치 모드 강제)
- **레거시 .hwp 입력 호환 유지** (실시간 모드)
- 한/글 COM 호출 자체는 형식 무관 — 한/글이 자동 처리

## 기타

- **runtime LLM-free**: 룰 실행에 LLM·MCP 모두 불필요. LLM 은 룰 작성(개발
  시점)에만 활용
- 상세 협업 가이드는 [CLAUDE.md](CLAUDE.md), md 입력 사양은
  [spec/markdown-spec.md](spec/markdown-spec.md) 참조
