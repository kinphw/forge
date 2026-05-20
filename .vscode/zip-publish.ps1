# publish/fdd/Forge.exe -> publish/Forge-v{version}.zip
# Called by .vscode/tasks.json publish:fdd:zip task.
# (publish:fdd must run first via dependsOn so fdd/Forge.exe exists.)
# Version is read from csproj <Version> as the single source of truth.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'src/Forge.UI/Forge.UI.csproj'
$exe = Join-Path $root 'publish/fdd/Forge.exe'

if (-not (Test-Path $exe)) {
    Write-Host '  [error] publish/fdd/Forge.exe not found - run publish:fdd first' -ForegroundColor Red
    exit 1
}

$xml = [xml](Get-Content $csproj)
$ver = $xml.SelectSingleNode('//Version').InnerText
$zip = Join-Path $root ('publish/Forge-v' + $ver + '.zip')

if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $exe -DestinationPath $zip

$kb = [math]::Round((Get-Item $zip).Length / 1KB, 1)
Write-Host ''
Write-Host ('  OK ' + $zip + ' (' + $kb + ' KB)') -ForegroundColor Green
