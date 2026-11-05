# Builds the tool and opens its window.
# Without -NoElevate Windows asks for administrator rights, and the tool installs its sensor driver and removes it on exit.
param([switch]$NoElevate)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/AkikazuHardwareCheck.App/AkikazuHardwareCheck.App.csproj'

dotnet build $project -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $root 'src/AkikazuHardwareCheck.App/bin/Debug/net10.0-windows/win-x64/AkikazuHardwareCheck.exe'
if ($NoElevate) {
    Start-Process $exe -ArgumentList '--no-elevate'
} else {
    Start-Process $exe
}
