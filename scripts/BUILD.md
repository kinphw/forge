# Forge — PyInstaller 배포 빌드 안내

격리된 venv 에서 PyInstaller 로 단일 실행파일(.exe) 을 만드는 절차입니다.
시스템 Python 환경을 오염시키지 않으며, 빌드 후 `.venv-build/` 만 지우면
원상복구됩니다.

## 1. 자동 (권장)

PowerShell 에서:

```powershell
# 기본 — onefile + windowed (콘솔창 없음)
pwsh ./scripts/build.ps1

# 처음부터 깨끗하게 다시 빌드
pwsh ./scripts/build.ps1 -Clean

# onedir (시작 빠름, 폴더째 배포)
pwsh ./scripts/build.ps1 -OneDir

# 디버그 — 콘솔창 표시 (예외 메시지 보고 싶을 때)
pwsh ./scripts/build.ps1 -KeepConsole
```

산출물:

| 모드 | 경로 |
|---|---|
| onefile (기본) | `dist/Forge.exe` |
| onedir | `dist/Forge/Forge.exe` (전체 폴더 배포) |

## 2. 수동 (스크립트 없이)

```powershell
# (1) 격리된 venv 생성
py -3.11 -m venv .venv-build
.\.venv-build\Scripts\Activate.ps1

# (2) 런타임 의존성 + PyInstaller 만 설치 (dev extras 제외 → 슬림)
python -m pip install --upgrade pip wheel
python -m pip install . pyinstaller

# (3) 빌드 — 최소 산출물
pyinstaller --noconfirm --clean --onefile --windowed `
    --name Forge `
    --exclude-module tkinter.test --exclude-module test `
    --exclude-module unittest --exclude-module pydoc `
    --exclude-module numpy --exclude-module pandas --exclude-module PIL `
    --exclude-module pytest --exclude-module ruff --exclude-module mypy `
    --collect-submodules win32com `
    --collect-submodules pythoncom `
    --collect-submodules pywintypes `
    run.py

# (4) 산출물 확인
dir .\dist\Forge.exe
```

## 3. 옵션 해설

- `--onefile` : 단일 .exe — 배포 편하지만 매 실행마다 임시폴더 압축 해제 → 시작 ~1–3 초.
- `--onedir`  : 폴더째 배포 — 시작 빠름, 파일 수 많음.
- `--windowed` : 콘솔창 숨김 (GUI 앱 필수). 디버그 중에는 빼서 stdout 확인.
- `--exclude-module` : 사용 안 하는 모듈을 빌드에서 제외 → exe 크기↓.
- `--collect-submodules win32com` : `win32com.client.Dispatch` 의 동적 import 가
  PyInstaller 정적 분석에 안 잡히므로 명시 수집. **한/글 COM 호출에 필수**.
- venv 분리(`.venv-build/`) : 개발용 venv 와 격리. 빌드 venv 에는 dev 의존성을
  넣지 않아 산출물에 불필요 모듈이 섞이지 않음.

## 4. 흔한 문제

| 증상 | 해결 |
|---|---|
| 실행 시 `ModuleNotFoundError: win32com` 등 | `--collect-submodules win32com pythoncom pywintypes` 빠짐 |
| 첫 실행 시 한/글 attach 실패 | exe 와 별개 — 한/글이 미실행. 한/글 먼저 실행 |
| 안티바이러스가 .exe 격리 | PyInstaller onefile 의 알려진 false positive. onedir 로 빌드하거나 코드 서명 |
| 크기가 80MB 초과 | `pip list` 로 venv 내 큰 패키지 확인. 빌드 venv 를 깨끗이 다시 생성(`-Clean`) |
| 한/글 COM 호출 시 `CoInitialize has not been called` | 코드 측 이슈 — `forge.hwp_session.init_com_for_thread()` 호출 누락 (빌드 무관) |

## 5. 정리

빌드 산출물 + 빌드 venv 모두 제거:

```powershell
pwsh ./scripts/build.ps1 -Clean
# 또는 수동:
Remove-Item -Recurse -Force .venv-build, build, dist, Forge.spec
```

`.gitignore` 에 이미 `build/`, `dist/`, `*.spec`, `.venv*/` 가 포함되어 있다면
별도 조치 불필요. 없으면 추가 권장.
