# Publie une nouvelle version d'Arcadia pour vos amis.
#
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Notes "- Nouveaux thèmes`n- Corrections"
#
# Étapes : numéro de version dans Arcadia.csproj -> tests -> Arcadia.exe -> commit + tag Git (+ envoi sur
# GitHub) -> Release sur le dépôt des versions (UpdateRepository, ici le même dépôt que le code) avec
# Arcadia.exe en pièce jointe. Les Arcadia déjà installés la proposent ensuite au démarrage.
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Notes = "",
    [string]$NotesFile,
    [switch]$SkipTests,
    [switch]$NoPush
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$csproj = Join-Path $root "src\Arcadia\Arcadia.csproj"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version attendue au format 1.2.3 (reçu : $Version)" }
$tag = "v$Version"
if ($NotesFile) { $Notes = Get-Content $NotesFile -Raw -Encoding UTF8 }
if (-not $Notes) { $Notes = "Arcadia $Version" }

# --- dépôt des versions -------------------------------------------------------------------------------
$xml = [xml](Get-Content $csproj -Raw -Encoding UTF8)
$repo = ($xml.Project.PropertyGroup | ForEach-Object { $_.UpdateRepository } | Where-Object { $_ }) | Select-Object -First 1
if (-not $repo -or $repo -like "VOTRE-PSEUDO*") {
    throw "Indiquez d'abord votre dépôt public des versions dans Arcadia.csproj, ex. <UpdateRepository>pseudo/arcadia</UpdateRepository>"
}
$current = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
Push-Location $root
try {
    # la toute première publication peut garder la version actuelle (1.0.0) ; ensuite elle doit augmenter
    $firstRelease = -not (git tag --list "v*")
    if ([version]$Version -lt [version]$current -or ([version]$Version -eq [version]$current -and -not $firstRelease)) {
        throw "La version $Version doit être plus récente que la version actuelle ($current)"
    }
    if (git status --porcelain) { throw "Des modifications ne sont pas enregistrées dans Git : faites d'abord un commit (git add -A ; git commit -m ...)" }
    if (git tag --list $tag) { throw "Le tag $tag existe déjà" }

    # --- numéro de version ---------------------------------------------------------------------------
    $text = Get-Content $csproj -Raw -Encoding UTF8
    $text = $text -replace "<Version>[^<]*</Version>", "<Version>$Version</Version>"
    [IO.File]::WriteAllText($csproj, $text, (New-Object Text.UTF8Encoding($true)))
    Write-Host "Version $current -> $Version" -ForegroundColor Cyan

    # --- tests et exécutable -------------------------------------------------------------------------
    $local = "$env:LOCALAPPDATA\Microsoft\dotnet"
    if (Test-Path "$local\dotnet.exe") { $env:DOTNET_ROOT = $local; $env:PATH = "$local;$env:PATH" }
    if (-not $SkipTests) {
        dotnet test (Join-Path $root "tests\Arcadia.Tests\Arcadia.Tests.csproj") --nologo -v q
        if ($LASTEXITCODE -ne 0) { git checkout -- $csproj; throw "Les tests échouent : version annulée" }
    }
    & (Join-Path $PSScriptRoot "publish.ps1")
    $exe = Join-Path $root "publish\Arcadia.exe"
    if (-not (Test-Path $exe)) { git checkout -- $csproj; throw "Arcadia.exe n'a pas été produit" }

    # --- Git : commit + tag (+ envoi sur GitHub) ------------------------------------------------------
    git add $csproj
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) { git commit -m "Arcadia $Version" | Out-Null }
    git tag -a $tag -m "Arcadia $Version"
    if (-not $NoPush -and (git remote)) {
        git push
        git push origin $tag
    }

    # --- Release publique avec Arcadia.exe ------------------------------------------------------------
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        gh release create $tag $exe --repo $repo --title "Arcadia $Version" --notes $Notes
        if ($LASTEXITCODE -ne 0) { throw "La création de la Release a échoué (gh auth status ?)" }
        Write-Host "Publié : https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
    }
    else {
        Write-Host ""
        Write-Host "GitHub CLI (gh) absent : terminez la publication à la main :" -ForegroundColor Yellow
        Write-Host "  1. Ouvrez https://github.com/$repo/releases/new?tag=$tag"
        Write-Host "  2. Titre : Arcadia $Version   -   Notes : vos nouveautés"
        Write-Host "  3. Glissez le fichier $exe puis « Publish release »"
    }
}
finally { Pop-Location }
