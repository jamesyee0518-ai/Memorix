param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,

    [string]$SiteRoot = "C:\Memorix",

    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable"
)

$ErrorActionPreference = "Stop"

$sourceRoot = (Resolve-Path $PackageRoot).Path
$sourceManifest = Join-Path $sourceRoot "$Channel\latest.json"
$sourceReleases = Join-Path $sourceRoot "releases"

if (-not (Test-Path $sourceManifest -PathType Leaf)) {
    throw "Update manifest not found: $sourceManifest"
}
if (-not (Test-Path $sourceReleases -PathType Container)) {
    throw "Versioned release directory not found: $sourceReleases"
}

$manifest = Get-Content $sourceManifest -Raw | ConvertFrom-Json
if (-not $manifest.version -or -not $manifest.platforms.'darwin-aarch64' -or -not $manifest.platforms.'windows-x86_64') {
    throw "latest.json is missing version or required platform entries"
}

$targetRoot = Join-Path $SiteRoot "desktop-updates"
$targetReleases = Join-Path $targetRoot "releases"
$targetChannel = Join-Path $targetRoot $Channel
$targetVersion = Join-Path $targetReleases $manifest.version

New-Item -ItemType Directory -Force -Path $targetVersion | Out-Null
New-Item -ItemType Directory -Force -Path $targetChannel | Out-Null

Copy-Item -Path (Join-Path $sourceReleases $manifest.version "*") `
    -Destination $targetVersion -Recurse -Force

$temporaryManifest = Join-Path $targetChannel "latest.json.next"
Copy-Item -Path $sourceManifest -Destination $temporaryManifest -Force

$publishedManifest = Join-Path $targetChannel "latest.json"
Move-Item -Path $temporaryManifest -Destination $publishedManifest -Force

Write-Host "Memorix desktop update $($manifest.version) published to $Channel."
Write-Host "Manifest: $publishedManifest"
