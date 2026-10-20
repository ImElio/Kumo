<#
.SYNOPSIS
  Builds the NSIS installers and SHA256SUMS.txt for every supported architecture.
.DESCRIPTION
  Output names follow the convention the updater relies on (see docs/releases.md):
    Kumo-<version>-win-<arch>-setup.exe, SHA256SUMS.txt
  The update repository is embedded so the build knows where its releases come from; leave it empty for local builds.
#>
param(
  [string]$Repository = "",
  [string]$Publisher = "",
  [string]$SourceRevision = "",
  [string[]]$Runtimes = @("win-x64", "win-arm64"),
  [string]$OutDir = "artifacts/release",
  # Build for public download: the build fails unless the update repository is configured.
  [switch]$PublicRelease,
  # Optional signing command for the installer and uninstaller, with %1 standing for the file to sign.
  [string]$SignCommand = "",
  # Development builds only: appended as a pre-release (for example dev.42) so successive builds order correctly.
  [string]$PrereleaseSuffix = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($PublicRelease -and $PrereleaseSuffix) { throw "Development builds are never public releases." }
$check = & (Join-Path $PSScriptRoot "check-version.ps1")
$version = ($check | Where-Object { $_ -like "version=*" }).Split("=")[1]
$numeric = ($check | Where-Object { $_ -like "numeric=*" }).Split("=")[1]
if ($PrereleaseSuffix) {
  if ($PrereleaseSuffix -notmatch '^dev\.(0|[1-9]\d*)$') { throw "PrereleaseSuffix must look like dev.<number>." }
  $version = if ($version.Contains("-")) { "$version.$PrereleaseSuffix" } else { "$version-$PrereleaseSuffix" }
}

$makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
  $makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makensis) { throw "NSIS (makensis) is required to build the installer." }

$out = Join-Path $root $OutDir
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

foreach ($runtime in $Runtimes) {
  $publish = Join-Path $root "artifacts/publish-$runtime"
  if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
  $arguments = @(
    "publish", (Join-Path $root "src/Kumo.App/Kumo.App.csproj"),
    "-c", "Release", "-r", $runtime, "--self-contained", "true",
    "-o", $publish,
    "-p:KumoUpdateRepository=$Repository",
    "-p:KumoUpdatePublisher=$Publisher",
    "-p:ContinuousIntegrationBuild=true",
    "-p:Version=$version"
  )
  if ($PublicRelease) { $arguments += "-p:KumoPublicRelease=true" }
  if ($SourceRevision) { $arguments += "-p:SourceRevisionId=$SourceRevision" }
  & dotnet @arguments
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtime." }

  $arch = $runtime.Replace("win-", "")
  $nsis = @(
    "/V2",
    "/DVERSION=$version", "/DVERSION_NUMERIC=$numeric", "/DARCH=$arch",
    "/DSOURCE_DIR=$publish",
    "/DOUTFILE=$(Join-Path $out "Kumo-$version-$runtime-setup.exe")",
    "/DICON=$(Join-Path $root "assets/icons/kumo.ico")",
    "/DLICENSE_FILE=$(Join-Path $root "LICENSE")"
  )
  if ($SignCommand) { $nsis += "/DSIGN_COMMAND=$SignCommand" }
  & $makensis @nsis (Join-Path $root "packaging/installer/Kumo.nsi")
  if ($LASTEXITCODE -ne 0) { throw "makensis failed for $runtime." }
}

$lines = Get-ChildItem $out -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object Name | ForEach-Object {
  "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[System.IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), (($lines -join "`n") + "`n"))
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Output
