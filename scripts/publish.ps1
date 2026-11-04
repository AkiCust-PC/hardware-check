# Builds the portable release: one self-contained exe plus license texts and a SHA-256 file, zipped for GitHub Release.
# The zip name has no version, so releases/latest/download/<name> always points at the newest release.
# Output: artifacts/release/AkikazuHardwareCheck-<version>-win-x64/ and artifacts/release/AkikazuHardwareCheck-win-x64.zip
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/AkikazuHardwareCheck.App/AkikazuHardwareCheck.App.csproj'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$output = Join-Path $root "artifacts/release/AkikazuHardwareCheck-$version-win-x64"

if (Test-Path $output) {
    Remove-Item -Recurse -Force $output
}

# Single-file settings live in the project, so plain builds and publishing restore the same lock files.
dotnet publish $project -c Release --self-contained true -o $output --nologo -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$licenses = Join-Path $output 'licenses'
Copy-Item -Recurse (Join-Path $root 'licenses') $licenses
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $output
Copy-Item (Join-Path $root 'LICENSE') $output

# A self-contained build ships the .NET runtime, so its own notices come from the SDK that built it.
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
foreach ($name in 'LICENSE.txt', 'ThirdPartyNotices.txt') {
    Copy-Item (Join-Path $dotnetRoot $name) (Join-Path $licenses "dotnet-$name")
}

$exe = Join-Path $output 'AkikazuHardwareCheck.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Set-Content -Path (Join-Path $output 'SHA256.txt') -Value "$hash  AkikazuHardwareCheck.exe" -Encoding ascii

$zip = Join-Path $root 'artifacts/release/AkikazuHardwareCheck-win-x64.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash

Write-Output "Version: $version"
Write-Output "Size:    $([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB"
Write-Output "SHA-256: $hash"
Write-Output "Output:  $output"
Write-Output "Zip:     $zip"
Write-Output "Zip size: $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
Write-Output "Zip SHA-256: $zipHash"
