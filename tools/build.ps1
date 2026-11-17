# Builds Arcadia with the SDK installed in %LOCALAPPDATA%\Microsoft\dotnet (falls back to dotnet on PATH).
param([string]$Project = "src\Arcadia\Arcadia.csproj", [string]$Configuration = "Debug")
$local = "$env:LOCALAPPDATA\Microsoft\dotnet"
if (Test-Path "$local\dotnet.exe") { $env:DOTNET_ROOT = $local; $env:PATH = "$local;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"; $env:DOTNET_NOLOGO = "1"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet build $Project -c $Configuration -clp:ErrorsOnly -nologo
exit $LASTEXITCODE
