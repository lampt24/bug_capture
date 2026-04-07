# BugCapture Release Guide

This guide explains how to create a release with scripts/create-release.ps1,
upload assets to GitHub Release, and update update-feed.json for in-app auto update.

## 1) Prerequisites

- PowerShell 7+ (`pwsh`)
- .NET SDK (compatible with project)
- Git configured with remote `origin`
- GitHub token with `repo` permission

Set token in one of these ways:

```powershell
$env:GITHUB_TOKEN="YOUR_PAT"
```

or pass directly:

```powershell
-GitHubToken "YOUR_PAT"
```

## 2) Script location

- Script: scripts/create-release.ps1
- Feed file updated by script: update-feed.json

You can run the script from repo root or from scripts folder.

## 3) Basic usage

### A. Auto version (recommended)

Default bump is `patch`.

```powershell
pwsh -File .\scripts\create-release.ps1 -Repo lampt24/bug_capture -ReleaseNotes "Release auto" -AllowDirty
```

Custom bump:

```powershell
pwsh -File .\scripts\create-release.ps1 -Repo lampt24/bug_capture -Bump minor -ReleaseNotes "Release auto minor" -AllowDirty
```

`-Bump` supports: `patch`, `minor`, `major`.

### B. Fixed version

```powershell
pwsh -File .\scripts\create-release.ps1 -Version 1.0.1 -Repo lampt24/bug_capture -ReleaseNotes "Release v1.0.1" -AllowDirty
```

## 4) What the script does

1. Resolve repository root and paths.
2. Determine release version:
   - Use `-Version` if provided.
   - Otherwise auto-increment from latest tag/project version by `-Bump`.
3. Update project metadata in BugCapture.csproj:
   - `Version`
   - `AssemblyVersion`
   - `FileVersion`
   - `InformationalVersion`
4. Publish build for selected runtime.
5. Zip artifact to artifacts folder.
6. Create/push git tag (unless skipped).
7. Create or reuse GitHub Release.
8. Upload zip asset to release.
9. Update update-feed.json with latest:
   - version
   - downloadUrl
   - notes
   - sha256
10. Optional: commit/push feed file (`-CommitFeed`).

## 5) Useful options

- `-Runtime win-x64` (default)
- `-SelfContained`
- `-SkipTag`
- `-SkipPushTag`
- `-AllowDirty`
- `-CommitFeed`
- `-FeedCommitMessage "..."`
- `-GitHubToken "..."`

Example with feed auto commit:

```powershell
pwsh -File .\scripts\create-release.ps1 `
  -Repo lampt24/bug_capture `
  -Bump patch `
  -ReleaseNotes "Release auto" `
  -AllowDirty `
  -CommitFeed
```

## 6) Common issues

### 6.1 Token missing

Error:

`GitHub token is missing. Running local release mode (zip + feed update only).`

Behavior:

- Script still publishes and creates zip in `artifacts/`.
- `update-feed.json` is still updated (`version`, `sha256`, `notes`, `downloadUrl`).
- GitHub release/tag upload steps are skipped.

If you need GitHub Release asset upload, set `GITHUB_TOKEN` or pass `-GitHubToken`.

### 6.2 Dirty working tree

Error:

`Working tree is not clean...`

Fix:

- commit/stash current changes, or
- run with `-AllowDirty`.

### 6.3 Parameter typo

Make sure there is a space before each parameter.

Correct:

```powershell
-ReleaseNotes "Release v0.1.3" -AllowDirty
```

Incorrect:

```powershell
-ReleaseNotes "Release v0.1.3"-AllowDirty
```

### 6.4 Update popup not shown although new release exists

Check:

- Auto update enabled in app settings.
- AutoUpdateFeedUrl is not empty.
- Running app version is lower than feed version.

Important: if app version is `1.0.0.0`, then feed `0.1.x` is not newer.

## 7) Recommended release flow

1. Run auto-version release script.
2. Verify release asset appears on GitHub.
3. Ensure update-feed.json in repo has latest values.
4. Open app and verify update prompt.

## 8) Quick command set

```powershell
$env:GITHUB_TOKEN="YOUR_PAT"
pwsh -File .\scripts\create-release.ps1 -Repo lampt24/bug_capture -Bump patch -ReleaseNotes "Release auto" -AllowDirty -CommitFeed
```
