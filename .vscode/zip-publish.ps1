# publish/fdd/Forge.exe -> publish/Forge-v{version}.zip
# Called by .vscode/tasks.json publish:fdd:zip task.
# (publish:fdd must run first via dependsOn so fdd/Forge.exe exists.)
# Version SSOT: Directory.Build.props 의 <Version> (모든 csproj 가 상속).

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$propsFile = Join-Path $root 'Directory.Build.props'
$exe = Join-Path $root 'publish/fdd/Forge.exe'

if (-not (Test-Path $exe)) {
    Write-Host '  [error] publish/fdd/Forge.exe not found - run publish:fdd first' -ForegroundColor Red
    exit 1
}

$xml = [xml](Get-Content $propsFile)
$verNode = $xml.SelectSingleNode('//Version')
if (-not $verNode -or [string]::IsNullOrWhiteSpace($verNode.InnerText)) {
    Write-Host "  [error] <Version> not found in $propsFile" -ForegroundColor Red
    exit 1
}
$ver = $verNode.InnerText.Trim()
$zip = Join-Path $root ('publish/Forge-v' + $ver + '.zip')

if (Test-Path $zip) { Remove-Item $zip }

# Forge.exe + 양식삽입 미리보기 PNG 폴더 (있을 때만) 함께 아카이브.
$paths = @($exe)
$resDir = Join-Path $root 'publish/fdd/resources'
if (Test-Path $resDir) { $paths += $resDir }
Compress-Archive -Path $paths -DestinationPath $zip

$kb = [math]::Round((Get-Item $zip).Length / 1KB, 1)
Write-Host ''
Write-Host ('  OK ' + $zip + ' (' + $kb + ' KB)') -ForegroundColor Green
