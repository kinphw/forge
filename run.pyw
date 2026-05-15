"""
Forge GUI 진입점.

확장자 .pyw — Windows 에서 pythonw.exe 로 실행되어 콘솔창 없이 GUI 만 띄움.
더블클릭 실행 또는 `python run.pyw` / `pythonw run.pyw` 로도 동작.

또는 (pyproject.toml 설치 후):
    sentinel-forge
"""
import sys

from forge.ui.app import main

if __name__ == "__main__":
    sys.exit(main())
