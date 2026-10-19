<#
.SYNOPSIS
  Builds the portable release ZIPs and SHA256SUMS.txt for every supported architecture.
.DESCRIPTION
  Output names follow the convention the updater relies on (see docs/releases.md):
    Kumo-<version>-win-x64.zip, Kumo-<version>-win-arm64.zip, SHA256SUMS.txt
  The update repository is embedded so the build knows where its releases come from; leave it empty for local builds.
#>
param(
  [string]$Repository = "",
  [string]$Publisher = "",
  [string]$SourceRevision = "",
  [string[]]$Runtimes = @("win-x64", "win-arm64"),
  [string]$OutDir = "artifacts/release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$check = & (Join-Path $PSScriptRoot "check-version.ps1")
$version = ($check | Where-Object { $_ -like "version=*" }).Split("=")[1]

$out = Join-Path $root $OutDir
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

foreach ($runtime in $Runtimes) {
  $publish = Join-Path $root "artifacts/publish-$runtime"
  if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
  $arguments = @(
    "publish", (Join-Path $root "src/Kumo.App/Kumo.App.csproj"),
    "-c", "Release", "-r", $runtime, "--self-contained", "false",
    "-o", $publish,
    "-p:KumoUpdateRepository=$Repository",
    "-p:KumoUpdatePublisher=$Publisher",
    "-p:ContinuousIntegrationBuild=true"
  )
  if ($SourceRevision) { $arguments += "-p:SourceRevisionId=$SourceRevision" }
  & dotnet @arguments
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $runtime." }

  $zip = Join-Path $out "Kumo-$version-$runtime.zip"
  Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal
}

$lines = Get-ChildItem $out -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object Name | ForEach-Object {
  "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[System.IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), (($lines -join "`n") + "`n"))
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Output
