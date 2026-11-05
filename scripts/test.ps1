# Builds everything and runs the unit tests. They run as their own program because dotnet test's test host can be refused.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'AkikazuHardwareCheck.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

& (Join-Path $root 'tests/AkikazuHardwareCheck.Core.Tests/bin/Release/net10.0-windows/win-x64/AkikazuHardwareCheck.Core.Tests.exe')
exit $LASTEXITCODE
