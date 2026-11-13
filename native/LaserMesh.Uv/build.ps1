param([string]$ZigPath = $env:ZIG_EXE)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$taskOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
if ($ZigPath) {
    & $ZigPath c++ -target x86_64-windows-gnu -O2 -std=c++17 -shared `
        -I (Join-Path $taskRoot 'external/xatlas/source/xatlas') `
        (Join-Path $PSScriptRoot 'bridge.cpp') (Join-Path $taskRoot 'external/xatlas/source/xatlas/xatlas.cpp') `
        -o (Join-Path $taskOutput 'LaserMesh.Uv.Native.dll')
} else {
    & (Join-Path $PSScriptRoot 'build.cmd')
}
if ($LASTEXITCODE -ne 0) { throw "Native UV compilation failed: $LASTEXITCODE" }
