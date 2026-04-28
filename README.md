# forge

## Ver

0.2.0 (260428)

## 기능

1. 개조식 markdown을 입력받아 한/글 보고서(.hwpx 신규 산출 / .hwp 호환 입력)로 변환

- 개조식 md, 또는 사람이 직접 작성한 md를 동일 파이프라인으로 처리. md 사양 외에는 Sentinel과 코드 의존 없음.

2. 실시간 HWPX COM API 조작을 통해 문서편집 효율화 

## 사용 모드 (2가지)

사용자 관점에서 Forge는 두 가지 기능만 제공한다.

- **배치 모드 — md → 완성 hwpx**
  - `sentinel-forge build report.md --output report.hwpx`
  - 개조식 md(Sentinel 산출 또는 손글)를 받아 **변환·정규화·polishing 까지 한 번에** 처리, .hwpx 완성본 산출
  - 출력은 항상 .hwpx

- **실시간 모드 — 활성 문서 polishing**
  - `sentinel-forge polish-active --rule <rule>`
  - 이미 한/글에 열려 있는 문서(.hwp 또는 .hwpx)에 사용자가 룰을 골라 적용
  - 출력은 입력 형식 보존

## 형식 정책 (HWP / HWPX)

정부 HWP 단계적 퇴출 의사결정(2026-04) 반영:
- **신규 산출물 = .hwpx** (배치 모드 강제)
- **레거시 .hwp 입력 호환 유지** (실시간 모드)
- 한/글 COM 호출 자체는 형식 무관 — 한/글이 자동 처리  

## 기타

- **runtime LLM-free** : STAGE 1·2·3 실행에는 LLM·MCP 모두 불필요. LLM은 룰 작성(개발 시점)에만 활용
- 상세 협업 가이드는 [CLAUDE.md](CLAUDE.md), md 입력 사양은 [spec/markdown-spec.md](spec/markdown-spec.md) 참조
