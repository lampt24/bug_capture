param(
  [string]$Version,

  [Parameter(Mandatory = $true)]
  [string]$Repo,

  [string]$Runtime = "win-x64",
  [string]$ProjectPath = ".\\BugCapture.csproj",
  [string]$FeedPath = ".\\update-feed.json",
  [string]$ReleaseName,
  [string]$ReleaseNotes = "",
  [string]$GitHubToken,
  [ValidateSet("patch", "minor", "major")]
  [string]$Bump = "patch",
  [switch]$SelfContained,
  [switch]$SkipTag,
  [switch]$SkipPushTag,
  [switch]$AllowDirty,
  [switch]$CommitFeed,
  [string]$FeedCommitMessage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
  param([string]$Message)
  Write-Host "[release] $Message" -ForegroundColor Cyan
}

function Get-AuthHeaders {
  param([string]$Token)
  return @{
    Authorization = "Bearer $Token"
    Accept = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent" = "BugCapture-Release-Script"
  }
}

function Ensure-Version {
  param([string]$InputVersion)
  if ([string]::IsNullOrWhiteSpace($InputVersion)) {
    throw "Version is required."
  }

  $normalized = $InputVersion.Trim()
  if ($normalized.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalized = $normalized.Substring(1)
  }

  $parsed = $null
  if (-not [System.Version]::TryParse($normalized, [ref]$parsed)) {
    throw "Invalid version: $InputVersion"
  }

  return $parsed.ToString()
}

function Read-ProjectVersion {
  param([string]$ProjectFilePath)

  if (-not (Test-Path $ProjectFilePath)) {
    return $null
  }

  try {
    [xml]$xml = Get-Content -Path $ProjectFilePath -Raw
    $versionNode = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $versionNode) {
      return $null
    }

    $normalized = Ensure-Version -InputVersion $versionNode
    return [System.Version]::Parse($normalized)
  }
  catch {
    return $null
  }
}

function Get-LatestTaggedVersion {
  $tags = git tag --list "v*"
  if (-not $tags) {
    return $null
  }

  $versions = New-Object System.Collections.Generic.List[System.Version]
  foreach ($tagValue in $tags) {
    if ([string]::IsNullOrWhiteSpace($tagValue)) {
      continue
    }

    $trimmed = $tagValue.Trim()
    if ($trimmed.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
      $trimmed = $trimmed.Substring(1)
    }

    $parsed = $null
    if ([System.Version]::TryParse($trimmed, [ref]$parsed) -and $parsed -ne $null) {
      [void]$versions.Add($parsed)
    }
  }

  if ($versions.Count -eq 0) {
    return $null
  }

  return ($versions | Sort-Object -Descending | Select-Object -First 1)
}

function Get-AutoVersion {
  param(
    [string]$ResolvedProjectPath,
    [string]$BumpKind
  )

  $latestTagVersion = Get-LatestTaggedVersion
  $projectVersion = Read-ProjectVersion -ProjectFilePath $ResolvedProjectPath

  $baseVersion = $latestTagVersion
  if ($projectVersion -and (($baseVersion -eq $null) -or ($projectVersion -gt $baseVersion))) {
    $baseVersion = $projectVersion
  }

  if ($baseVersion -eq $null) {
    $baseVersion = [System.Version]::Parse("0.1.0")
  }

  $major = $baseVersion.Major
  $minor = $baseVersion.Minor
  $build = if ($baseVersion.Build -ge 0) { $baseVersion.Build } else { 0 }

  switch ($BumpKind) {
    "major" {
      $major += 1
      $minor = 0
      $build = 0
    }
    "minor" {
      $minor += 1
      $build = 0
    }
    default {
      $build += 1
    }
  }

  return "$major.$minor.$build"
}

function Ensure-RepoFormat {
  param([string]$RepoValue)
  if ([string]::IsNullOrWhiteSpace($RepoValue) -or $RepoValue -notmatch "^[^/]+/[^/]+$") {
    throw "Repo must be in 'owner/name' format."
  }
}

function Resolve-RepoRelativePath {
  param(
    [string]$RepoRoot,
    [string]$InputPath
  )

  if ([System.IO.Path]::IsPathRooted($InputPath)) {
    return $InputPath
  }

  return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $InputPath))
}

function Resolve-Token {
  param([string]$InputToken)

  if (-not [string]::IsNullOrWhiteSpace($InputToken)) {
    return $InputToken.Trim()
  }

  if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    return $env:GITHUB_TOKEN.Trim()
  }

  if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    return $env:GH_TOKEN.Trim()
  }

  return ""
}

function Get-RepoRelativePath {
  param(
    [string]$RepoRoot,
    [string]$AbsolutePath
  )

  $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $AbsolutePath)
  return $relative.Replace("\\", "/")
}

function Set-ProjectVersion {
  param(
    [string]$ProjectFilePath,
    [string]$VersionValue
  )

  [xml]$xml = Get-Content -Path $ProjectFilePath -Raw
  if (-not $xml.Project) {
    throw "Invalid project file: $ProjectFilePath"
  }

  $propertyGroup = $xml.Project.PropertyGroup | Select-Object -First 1
  if (-not $propertyGroup) {
    $propertyGroup = $xml.CreateElement("PropertyGroup")
    [void]$xml.Project.AppendChild($propertyGroup)
  }

  function Set-OrCreateNode {
    param(
      [System.Xml.XmlElement]$Parent,
      [string]$Name,
      [string]$Value
    )

    $node = $Parent.SelectSingleNode($Name)
    if (-not $node) {
      $node = $xml.CreateElement($Name)
      [void]$Parent.AppendChild($node)
    }

    $node.InnerText = $Value
  }

  $assemblyVersion = "$VersionValue.0"
  Set-OrCreateNode -Parent $propertyGroup -Name "Version" -Value $VersionValue
  Set-OrCreateNode -Parent $propertyGroup -Name "AssemblyVersion" -Value $assemblyVersion
  Set-OrCreateNode -Parent $propertyGroup -Name "FileVersion" -Value $assemblyVersion
  Set-OrCreateNode -Parent $propertyGroup -Name "InformationalVersion" -Value $VersionValue

  $xml.Save($ProjectFilePath)
}

$token = Resolve-Token -InputToken $GitHubToken
if ([string]::IsNullOrWhiteSpace($token)) {
  throw "GitHub token is missing. Set -GitHubToken or env GITHUB_TOKEN/GH_TOKEN."
}

Ensure-RepoFormat -RepoValue $Repo

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
  throw "Cannot locate git repository root. Run script inside the repository."
}

$repoRoot = $repoRoot.Trim()
$resolvedProjectPath = Resolve-RepoRelativePath -RepoRoot $repoRoot -InputPath $ProjectPath
$resolvedFeedPath = Resolve-RepoRelativePath -RepoRoot $repoRoot -InputPath $FeedPath
$feedGitPath = Get-RepoRelativePath -RepoRoot $repoRoot -AbsolutePath $resolvedFeedPath

if (-not (Test-Path $resolvedProjectPath)) {
  throw "Project file not found: $resolvedProjectPath"
}

$normalizedVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
  Get-AutoVersion -ResolvedProjectPath $resolvedProjectPath -BumpKind $Bump
} else {
  Ensure-Version -InputVersion $Version
}

$tag = "v$normalizedVersion"
if ([string]::IsNullOrWhiteSpace($ReleaseName)) {
  $ReleaseName = $tag
}

Write-Step "Release version: $normalizedVersion"

Push-Location $repoRoot
try {
if (-not $AllowDirty) {
  $dirty = git status --porcelain | Where-Object {
    $_ -and $_ -notmatch "^\?\?\s+artifacts/" -and $_ -notmatch "^\s*M\s+update-feed\.json$"
  }
  if ($dirty) {
    throw "Working tree is not clean. Commit/stash changes first, or use -AllowDirty."
  }
}

Write-Step "Updating project version metadata to $normalizedVersion"
Set-ProjectVersion -ProjectFilePath $resolvedProjectPath -VersionValue $normalizedVersion

$root = (Resolve-Path ".").Path
$artifactsDir = Join-Path $root "artifacts"
$publishDir = Join-Path $artifactsDir "publish\\$Runtime"
$zipName = "BugCapture-$normalizedVersion-$Runtime.zip"
$zipPath = Join-Path $artifactsDir $zipName

Write-Step "Publishing app ($Runtime)..."
$publishArgs = @(
  "publish", $resolvedProjectPath,
  "-c", "Release",
  "-r", $Runtime,
  "--self-contained", ($SelfContained.IsPresent ? "true" : "false"),
  "-o", $publishDir
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed."
}

if (Test-Path $zipPath) {
  Remove-Item $zipPath -Force
}

Write-Step "Creating artifact zip: $zipName"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
if (-not (Test-Path $zipPath)) {
  throw "Failed to create artifact zip."
}

$sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash
Write-Step "Artifact SHA256: $sha256"

if (-not $SkipTag) {
  $existingTag = git tag --list $tag
  if (-not $existingTag) {
    Write-Step "Creating git tag: $tag"
    git tag -a $tag -m "Release $tag"
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to create git tag."
    }
  } else {
    Write-Step "Tag already exists locally: $tag"
  }

  if (-not $SkipPushTag) {
    Write-Step "Pushing tag to origin"
    git push origin $tag
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to push git tag."
    }
  }
}

$headers = Get-AuthHeaders -Token $token
$releaseBody = @{
  tag_name = $tag
  name = $ReleaseName
  body = $ReleaseNotes
  draft = $false
  prerelease = $false
  generate_release_notes = [string]::IsNullOrWhiteSpace($ReleaseNotes)
} | ConvertTo-Json

$releaseApi = "https://api.github.com/repos/$Repo/releases"

Write-Step "Creating or reusing GitHub release $tag"
$release = $null
try {
  $release = Invoke-RestMethod -Method Post -Uri $releaseApi -Headers $headers -ContentType "application/json" -Body $releaseBody
} catch {
  if ($_.Exception.Message -match "422") {
    Write-Step "Release already exists, reading existing release by tag"
    $release = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$Repo/releases/tags/$tag" -Headers $headers
  } else {
    throw
  }
}

if (-not $release -or -not $release.upload_url) {
  throw "Unable to get release upload URL."
}

$existingAsset = $null
if ($release.assets) {
  $existingAsset = $release.assets | Where-Object { $_.name -eq $zipName } | Select-Object -First 1
}

if ($existingAsset) {
  Write-Step "Deleting existing asset: $zipName"
  Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existingAsset.id)" -Headers $headers | Out-Null
}

$uploadUrl = $release.upload_url -replace "\{\?name,label\}", ""
$uploadUrl = "${uploadUrl}?name=$zipName"

Write-Step "Uploading release asset"
$bytes = [System.IO.File]::ReadAllBytes($zipPath)
$asset = Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -ContentType "application/zip" -Body $bytes

if (-not $asset -or -not $asset.browser_download_url) {
  throw "Asset upload succeeded but no download URL returned."
}

$feed = @{
  version = $normalizedVersion
  downloadUrl = $asset.browser_download_url
  notes = if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) { "Release $tag" } else { $ReleaseNotes }
  sha256 = $sha256
}

Write-Step "Updating feed file: $FeedPath"
$feedJson = $feed | ConvertTo-Json -Depth 5
Set-Content -Path $resolvedFeedPath -Value $feedJson -Encoding UTF8

if ($CommitFeed) {
  if ([string]::IsNullOrWhiteSpace($FeedCommitMessage)) {
    $FeedCommitMessage = "chore: update update-feed.json for $tag"
  }

  Write-Step "Committing feed update"
  git add -- $feedGitPath
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to stage feed file."
  }

  $feedDiff = git diff --cached --name-only
  if ($feedDiff) {
    git commit -m $FeedCommitMessage
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to commit feed update."
    }

    Write-Step "Pushing feed commit"
    git push
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to push feed commit."
    }
  } else {
    Write-Step "No feed changes to commit"
  }
}

Write-Step "Release completed"
Write-Host "Tag: $tag"
Write-Host "Asset: $zipPath"
Write-Host "DownloadUrl: $($asset.browser_download_url)"
Write-Host "Feed: $(Resolve-Path $resolvedFeedPath)"
Write-Host "SHA256: $sha256"
}
finally {
  Pop-Location
}
