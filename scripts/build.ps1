# Forge — venv + PyInstaller 자동 빌드 (Windows / PowerShell)
#
# 목적:
#   - 격리된 venv 를 만들고 (.venv-build/) 런타임 의존성만 설치
#   - PyInstaller 로 단일 실행파일(.exe) 생성 — 최소 산출물
#   - 시스템 Python 환경을 오염시키지 않음
#
# 사용:
#   pwsh ./scripts/build.ps1                 # 기본: onefile, windowed
#   pwsh ./scripts/build.ps1 -OneDir         # onedir 빌드 (시작 빠름, 폴더 배포)
#   pwsh ./scripts/build.ps1 -Clean          # build/ dist/ .venv-build/ 모두 제거 후 재빌드
#   pwsh ./scripts/build.ps1 -KeepConsole    # 콘솔창 표시 (디버그용)
#
# 산출물:
#   dist/Forge.exe           (onefile, 기본)
#   dist/Forge/Forge.exe     (onedir, -OneDir)
#
# 전제:
#   - Windows + Python 3.11+ (`py -3.11` 또는 `python` 이 PATH 에 있어야 함)
#   - 인터넷 (pip install)

[CmdletBinding()]
param(
    [switch]$OneDir,
    [switch]$Clean,
    [switch]$KeepConsole
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

Write-Host "==> Forge build" -ForegroundColor Cyan
Write-Host "    Project root : $ProjectRoot"

$VenvDir = Join-Path $ProjectRoot ".venv-build"
$DistDir = Join-Path $ProjectRoot "dist"
$BuildDir = Join-Path $ProjectRoot "build"
$SpecFile = Join-Path $ProjectRoot "Forge.spec"

if ($Clean) {
    Write-Host "==> Cleaning previous artifacts" -ForegroundColor Yellow
    foreach ($p in @($VenvDir, $DistDir, $BuildDir, $SpecFile)) {
        if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    }
}

# 1) venv 생성
# 후보 python 명령들을 순서대로 시도. py -3.X 가 실패해도 (해당 버전 없음 등)
# 다른 후보로 fallback. PowerShell 7+ 의 native command error 가 Stop 으로 잡지
# 않게 try/catch + LASTEXITCODE 만 체크.
if (-not (Test-Path $VenvDir)) {
    Write-Host "==> Creating venv at $VenvDir" -ForegroundColor Cyan
    $candidates = @(
        @("py", "-3.12"), @("py", "-3.11"), @("py", "-3"),
        @("python", $null), @("python3", $null)
    )
    $venvOk = $false
    foreach ($c in $candidates) {
        $cmd = $c[0]; $arg = $c[1]
        $exists = Get-Command $cmd -ErrorAction SilentlyContinue
        if (-not $exists) { continue }
        try {
            if ($arg) {
                & $cmd $arg -m venv $VenvDir 2>&1 | Out-Host
            } else {
                & $cmd -m venv $VenvDir 2>&1 | Out-Host
            }
        } catch {
            continue
        }
        if ($LASTEXITCODE -eq 0 -and (Test-Path $VenvDir)) {
            Write-Host ("    venv created via: " + $cmd + " " + $arg) -ForegroundColor Green
            $venvOk = $true
            break
        }
    }
    if (-not $venvOk) { throw "venv 생성 실패 — Python 3.x 가 PATH 에 없음" }
}

$VenvPy = Join-Path $VenvDir "Scripts\python.exe"
if (-not (Test-Path $VenvPy)) { throw "venv python 을 찾지 못함: $VenvPy" }

# 2) 의존성 설치 — 런타임 의존성 + pyinstaller. dev/extras 는 제외 (산출물 슬림).
Write-Host "==> Installing runtime dependencies + PyInstaller" -ForegroundColor Cyan
& $VenvPy -m pip install --upgrade pip wheel | Out-Host
& $VenvPy -m pip install . pyinstaller | Out-Host
if ($LASTEXITCODE -ne 0) { throw "pip install 실패" }

# 3) PyInstaller 옵션 구성
$Name = "Forge"
$Entry = Join-Path $ProjectRoot "run.pyw"

$PyiArgs = @(
    "--noconfirm",
    "--name", $Name,
    "--clean"
)

if ($OneDir) { $PyiArgs += "--onedir" } else { $PyiArgs += "--onefile" }
if (-not $KeepConsole) { $PyiArgs += "--windowed" }

# 최소 산출물 — 사용하지 않는 stdlib/3rd-party 모듈 제외 (크기·시작시간 단축)
$Excludes = @(
    "tkinter.test", "test", "unittest", "pydoc", "pydoc_data",
    "pytest", "ruff", "mypy",
    "numpy", "pandas", "matplotlib", "PIL", "IPython",
    "setuptools._vendor", "pip", "wheel",
    "_tkinter.test"
)
foreach ($m in $Excludes) { $PyiArgs += @("--exclude-module", $m) }

# pywin32 / win32com 의 동적 dispatch 를 위해 collect (한/글 COM 호출용)
$Collects = @("win32com", "pythoncom", "pywintypes")
foreach ($m in $Collects) { $PyiArgs += @("--collect-submodules", $m) }

# Hidden imports — 런타임 동적 import 되는 모듈
$Hidden = @("pkg_resources.py2_warn")
foreach ($m in $Hidden) { $PyiArgs += @("--hidden-import", $m) }

$PyiArgs += $Entry

Write-Host "==> Running PyInstaller" -ForegroundColor Cyan
Write-Host "    Args: $($PyiArgs -join ' ')"
& $VenvPy -m PyInstaller @PyiArgs
if ($LASTEXITCODE -ne 0) { throw "PyInstaller 실패" }

# 4) 결과 안내
Write-Host ""
Write-Host "==> Build complete" -ForegroundColor Green
if ($OneDir) {
    $out = Join-Path $DistDir "$Name\$Name.exe"
} else {
    $out = Join-Path $DistDir "$Name.exe"
}
if (Test-Path $out) {
    $size = (Get-Item $out).Length / 1MB
    Write-Host ("    {0}  ({1:N1} MB)" -f $out, $size) -ForegroundColor Green
} else {
    Write-Host "    (예상 산출물 경로를 찾지 못함 — dist/ 폴더 확인)" -ForegroundColor Yellow
}
