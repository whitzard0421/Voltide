param(
    [string]$Target
)

# Builds Voltide and drops a clean, runnable folder inside the project:
#   dist\Voltide.exe
#   dist\device-names.ini  (optional model names for shared receiver IDs)
#
# Default target is dist\ next to this script, so everything stays in one place.
# Override with: powershell -File pack.ps1 -Target "E:\somewhere"

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot   # this script lives next to build.ps1

if (-not $Target) { $Target = Join-Path $root 'dist' }

# 只检查本次构建和目标文件，不终止其他目录中的 Voltide。
$buildExe = Join-Path $root 'Voltide.exe'
$targetExe = [IO.Path]::GetFullPath((Join-Path $Target 'Voltide.exe'))
$running = Get-Process Voltide -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq $buildExe -or $_.Path -eq $targetExe
}
if ($running) { throw 'This source or target copy of Voltide is running. Exit that copy before packing.' }

Write-Host "Building..." -ForegroundColor Cyan
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$exe = Join-Path $root 'Voltide.exe'
if (-not (Test-Path $exe)) { throw "Voltide.exe not found" }

Write-Host "Packing to $Target" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $Target -Force | Out-Null

# 字体可嵌入 exe；可选的型号覆盖配置与 exe 一并分发。
Copy-Item $exe (Join-Path $Target 'Voltide.exe') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $Target 'LICENSE') -Force
$names = Join-Path $root 'device-names.ini'
$targetNames = Join-Path $Target 'device-names.ini'
# 已有目标配置属于使用者，重新打包时保留其本机命名。
if ((Test-Path -LiteralPath $names) -and -not (Test-Path -LiteralPath $targetNames)) {
    Copy-Item -LiteralPath $names -Destination $targetNames
}

Write-Host ""
Write-Host "--- packed ---" -ForegroundColor Green
Get-ChildItem $Target -Recurse -File |
    Select-Object @{n='Path';e={$_.FullName.Substring($Target.Length + 1)}}, @{n='KB';e={[math]::Round($_.Length / 1KB, 1)}} |
    Format-Table -AutoSize

$size = (Get-ChildItem $Target -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("total: {0:N1} MB" -f ($size / 1MB)) -ForegroundColor Gray
Write-Host ""
Write-Host "Run it with: $Target\Voltide.exe" -ForegroundColor Gray
