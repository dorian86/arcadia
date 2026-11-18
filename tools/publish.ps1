# Builds the distributable: a single self-contained Arcadia.exe (no .NET installation required).
# Output: <repo>\publish\Arcadia.exe
param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$local = "$env:LOCALAPPDATA\Microsoft\dotnet"
if (Test-Path "$local\dotnet.exe") { $env:DOTNET_ROOT = $local; $env:PATH = "$local;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"; $env:DOTNET_NOLOGO = "1"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $root "publish"
if (Test-Path $out) {
    # an Arcadia started from this folder may still be running: Windows lets a running exe be renamed
    # (not deleted); the new version removes "Arcadia.exe.old" at its next start
    $exeOut = Join-Path $out "Arcadia.exe"
    if (Test-Path $exeOut) {
        try { Remove-Item $exeOut -Force }
        catch {
            Remove-Item "$exeOut.old" -Force -ErrorAction SilentlyContinue
            Rename-Item $exeOut "Arcadia.exe.old"
            Write-Host "Arcadia.exe est en cours d'exécution : renommé en Arcadia.exe.old" -ForegroundColor Yellow
        }
    }
    Get-ChildItem $out -Exclude "Arcadia.exe.old" | Remove-Item -Recurse -Force
}

dotnet publish (Join-Path $root "src\Arcadia\Arcadia.csproj") -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $out "Arcadia.exe"
"{0}  ({1:N1} Mo)" -f $exe, ((Get-Item $exe).Length / 1MB)
