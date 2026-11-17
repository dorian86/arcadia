# Developer helper: runs the Debug build against a throw-away data folder and renders a PNG of the window.
# Example: .\tools\snapshot.ps1 -Data C:\Temp\arc -Out C:\Temp\all.png -Extra '--section','all'
param(
    [Parameter(Mandatory)] [string]$Data,
    [Parameter(Mandatory)] [string]$Out,
    [string[]]$Extra = @(),
    [int]$TimeoutSec = 300
)
$local = "$env:LOCALAPPDATA\Microsoft\dotnet"
if (Test-Path "$local\dotnet.exe") { $env:DOTNET_ROOT = $local }
$exe = Join-Path $PSScriptRoot "..\src\Arcadia\bin\Debug\net10.0-windows\Arcadia.exe"
$args = @('--data', "`"$Data`"", '--snapshot', "`"$Out`"") + ($Extra | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } })
$p = Start-Process $exe -ArgumentList $args -PassThru
if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-Process -Id $p.Id -Force; Write-Output "TIMEOUT" }
Write-Output "exit=$($p.ExitCode) snapshot=$(Test-Path $Out)"
$log = Join-Path $Data "logs\arcadia.log"
if (Test-Path $log) { Get-Content $log | Select-String "\[ERROR\]" | ForEach-Object { $_.Line.Substring(0, [Math]::Min(300, $_.Line.Length)) } | Select-Object -First 15 }
