@echo off
REM Forge 빌드/배포 스크립트 진입점.
REM 사용: build <명령>   예) build release
REM   dev      소스에서 바로 GUI 실행 (개발용)
REM   build    솔루션 전체 빌드
REM   test     단위 테스트 (xUnit)
REM   publish  FDD 단일 exe publish -^> publish\fdd\Forge.exe
REM   zip      publish + zip 패키징 -^> publish\Forge-v<version>.zip
REM   bump     버전 패치 +1 (Directory.Build.props 의 Version SSOT)
REM   release  bump + publish + zip (원스텝 릴리스)
REM 주의: 이 파일은 cp949 + CRLF 로 저장해야 한다 (cmd 배치 파서 제약).
setlocal
cd /d "%~dp0"

if "%~1"=="" goto :usage
if /i "%~1"=="dev"     goto :dev
if /i "%~1"=="build"   goto :build
if /i "%~1"=="test"    goto :test
if /i "%~1"=="publish" goto :publish
if /i "%~1"=="zip"     goto :zip
if /i "%~1"=="bump"    goto :bump
if /i "%~1"=="release" goto :release
echo [build] 알 수 없는 명령: %~1
goto :usage

:dev
dotnet run --project src\Forge.UI\Forge.UI.csproj
goto :eof

:build
dotnet build Forge.sln
goto :eof

:test
dotnet test Forge.sln --nologo
goto :eof

:publish
call :do_publish
if errorlevel 1 exit /b 1
goto :eof

:zip
call :do_publish
if errorlevel 1 exit /b 1
call :do_zip
if errorlevel 1 exit /b 1
goto :eof

:bump
call :do_bump
if errorlevel 1 exit /b 1
goto :eof

:release
call :do_bump
if errorlevel 1 exit /b 1
call :do_publish
if errorlevel 1 exit /b 1
call :do_zip
if errorlevel 1 exit /b 1
echo.
echo [build] 릴리스 완료. publish 폴더의 zip 을 확인하세요.
goto :eof

REM ===== 내부 서브루틴 =====
:do_bump
powershell -NoProfile -ExecutionPolicy Bypass -File ".vscode\bump-version.ps1"
exit /b %errorlevel%

:do_publish
REM Forge.exe 가 실행 중이면 산출물이 잠겨 publish 실패. 먼저 종료 (없으면 무시).
taskkill /F /IM Forge.exe >nul 2>&1
dotnet publish src\Forge.UI\Forge.UI.csproj -c Release -p:SelfContained=false -o publish\fdd
exit /b %errorlevel%

:do_zip
powershell -NoProfile -ExecutionPolicy Bypass -File ".vscode\zip-publish.ps1"
exit /b %errorlevel%

:usage
echo.
echo   build dev       소스에서 GUI 실행 (개발용)
echo   build build     솔루션 전체 빌드
echo   build test      단위 테스트
echo   build publish   FDD 단일 exe publish
echo   build zip       publish + zip 패키징
echo   build bump      버전 패치 +1
echo   build release   bump + publish + zip (원스텝)
echo.
exit /b 1
