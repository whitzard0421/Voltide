$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'obj'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $out 'KeyboardTests.exe'
$sources = @(
    'src\Core\Models.cs'
    'src\Core\DeviceNames.cs'
    'src\Interop\HidNative.cs'
    'src\Providers\IDeviceProvider.cs'
    'src\Providers\MchoseProvider.cs'
    'tests\KeyboardTests.cs'
) | ForEach-Object { Join-Path $root $_ }
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Keyboard tests did not compile.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Keyboard tests failed.' }
