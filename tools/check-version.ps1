<#
.SYNOPSIS
  Validates that every place carrying Kumo's version agrees with Directory.Build.props.
.PARAMETER Tag
  Optional release tag (for example v0.2.0-beta.1) that must match the version exactly.
#>
param([string]$Tag = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$semver = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$'

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($version -notmatch $semver) { throw "Directory.Build.props <Version> '$version' is not a SemVer 2.0.0 version without build metadata." }
$numeric = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
$prerelease = $Matches[4]

[xml]$manifest = Get-Content (Join-Path $root "src/Kumo.App/app.manifest")
$manifestVersion = $manifest.assembly.assemblyIdentity.version
if ($manifestVersion -ne $numeric) { throw "src/Kumo.App/app.manifest assemblyIdentity version '$manifestVersion' must be '$numeric'." }

if ($prerelease -and ($prerelease.Split('.')[0] -notin @("beta", "rc", "alpha", "dev"))) {
  throw "Pre-release '$prerelease' must start with beta, rc, alpha or dev."
}

if ($Tag) {
  if ($Tag -ne "v$version") { throw "Tag '$Tag' does not match the project version 'v$version'." }
  if ($prerelease -and $prerelease.Split('.')[0] -eq "dev") { throw "Development versions are never released; use beta or rc." }
}

Write-Output "version=$version"
Write-Output "numeric=$numeric"
Write-Output "prerelease=$([bool]$prerelease)".ToLowerInvariant()
