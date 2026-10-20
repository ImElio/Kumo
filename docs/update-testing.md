# End-to-end update test

A manual procedure that exercises the real updater against two test releases. Nothing in it runs automatically, and
it must never use the public repository: create a **private test repository** you own (for example
`<you>/kumo-update-test`) and delete it when finished.

Automated tests cover selection, checksums, signatures and the GitHub client with fake responses; only this
procedure proves the pieces work together against GitHub.

## Prerequisites

- The test repository, initialised with a README so `gh` has a branch to tag, with no rules or environments.
- NSIS installed locally and the `gh` CLI signed in to the account that owns the test repository.
- Optional: a code-signing certificate trusted by the test PC and its exact subject, to test the signature check as
  well. Without one, installed copies update after the SHA-256 and version checks only.

## 1. Build version A (installed)

```powershell
# Directory.Build.props and app.manifest at 0.1.0
pwsh tools/package-release.ps1 -PublicRelease -Runtimes win-x64 `
  -Repository "<you>/kumo-update-test" -Publisher "<certificate subject or empty>" `
  -SignCommand "<sign command or omit>" -OutDir artifacts/test-a
```

Install `artifacts/test-a/Kumo-0.1.0-win-x64-setup.exe`. Check About shows `0.1.0` and Settings → Updates shows
"GitHub" as the installation source.

## 2. Build version B (offered)

Set the version to `0.1.1` in both `Directory.Build.props` and `src/Kumo.App/app.manifest` **locally, without
committing**, then package again into `artifacts/test-b` with the same parameters. Revert the version change
afterwards.

## 3. Publish both to the test repository

```powershell
gh release create windows/v0.1.0 artifacts/test-a/* --repo <you>/kumo-update-test --title "Kumo for Windows 0.1.0" --notes "Test A" --latest=false
gh release create windows/v0.1.1 artifacts/test-b/* --repo <you>/kumo-update-test --title "Kumo for Windows 0.1.1" --notes "## What's Changed`n* Test **B** for https://github.com/<you>/kumo-update-test/pull/1" --latest=false
```

`gh release create` creates the tag in the **test** repository only. Never run these commands against the public one.

Optionally add noise that must be ignored: a `linux/v9.0.0` release, a `windows/v0.1.2-alpha.1` pre-release, and a
draft `windows/v0.2.0`.

## 4. Check

| Step | Expected |
| --- | --- |
| Settings → Updates → Check now (Release channel) | "Kumo 0.1.1 is available", What's new card with the 0.1.1 notes |
| Restart Kumo, wait ~10 s | Notification "Kumo 0.1.1 is available"; clicking it opens Settings on Updates |
| Switch to Development | Offers `0.1.2-alpha.1` if created; Linux and draft releases never appear |
| Switch back to Release | Offers `0.1.1` again, never a lower version |
| Kumo log (`%LOCALAPPDATA%\Kumo\logs`) | No request to any repository other than the test one |

## 5. Install path

Click **Get update**. Expected: download progress, SHA-256 and version checked (and the signature, if a publisher is
configured), the installer runs silently without an elevation prompt, and Kumo restarts as `0.1.1`. Then:

- Tamper test: upload a modified `SHA256SUMS.txt` to the 0.1.1 test release; the download must be refused.
- Version test: replace the 0.1.1 installer and its checksum line with the 0.1.0 installer; Kumo must refuse to run it.
- Signed builds only: replace the installer with an unsigned build of the same version; Kumo must refuse to run it.

## 6. Clean up

Uninstall Kumo, delete the test repository, delete `artifacts/test-a` and `artifacts/test-b`, and confirm
`git status` shows no version change.
