# Directory.Build.props 의 <Version> 패치 자리 +1 (버전 SSOT).
# build.cmd 의 bump / release 에서 호출.
#
# XML 라운드트립([xml].Save) 은 주석·들여쓰기·선언을 흔들 수 있어 정규식 치환 사용.
# props 파일은 UTF-8(BOM 없음) + 한글 주석 포함 → 같은 인코딩으로 되써야 주석이 안 깨짐.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$propsFile = Join-Path $root 'Directory.Build.props'

if (-not (Test-Path $propsFile)) {
    Write-Host "  [error] $propsFile not found" -ForegroundColor Red
    exit 1
}

$content = [System.IO.File]::ReadAllText($propsFile)
$rx = '<Version>(\d+)\.(\d+)\.(\d+)</Version>'
if ($content -notmatch $rx) {
    Write-Host '  [error] <Version>x.y.z</Version> not found in Directory.Build.props' -ForegroundColor Red
    exit 1
}

$old = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
$new = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"

$content = [regex]::Replace($content, $rx, "<Version>$new</Version>", 1)

# UTF-8 no BOM 으로 고정 (Set-Content 는 PS5.1 에서 ANSI 라 한글 주석 깨짐)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($propsFile, $content, $utf8NoBom)

Write-Host ('  OK version ' + $old + ' -> ' + $new) -ForegroundColor Green
