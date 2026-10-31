#!/usr/bin/env pwsh
# Сборка одного self-contained .exe в папку publish/

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet publish "$root/src/SimpleTransparentWindow.csproj" -c Release -o "$root/publish"

$exe = Join-Path $root 'publish/SimpleTransparentWindow.exe'
if (Test-Path $exe) {
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
    Write-Host "Готово: $exe ($mb МБ)" -ForegroundColor Green
}
