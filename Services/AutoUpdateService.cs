using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BugCapture.Services
{
  public sealed class RemoteUpdateInfo
  {
    public Version CurrentVersion { get; set; } = new Version(0, 0, 0, 0);
    public Version LatestVersion { get; set; } = new Version(0, 0, 0, 0);
    public string DownloadUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
  }

  public class AutoUpdateService
  {
    private readonly HttpClient _httpClient = new HttpClient();

    public async Task<RemoteUpdateInfo?> CheckForUpdateAsync(Version currentVersion, string feedUrl)
    {
      if (string.IsNullOrWhiteSpace(feedUrl))
      {
        return null;
      }

      using var response = await _httpClient.GetAsync(feedUrl.Trim());
      response.EnsureSuccessStatusCode();
      var content = await response.Content.ReadAsStringAsync();

      var manifest = JsonSerializer.Deserialize<UpdateFeedManifest>(content, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
      {
        return null;
      }

      if (!TryParseVersion(manifest.Version, out var latestVersion))
      {
        return null;
      }

      if (latestVersion <= currentVersion)
      {
        return null;
      }

      return new RemoteUpdateInfo
      {
        CurrentVersion = currentVersion,
        LatestVersion = latestVersion,
        DownloadUrl = manifest.DownloadUrl,
        Notes = manifest.Notes ?? string.Empty,
        Sha256 = manifest.Sha256 ?? string.Empty
      };
    }

    public async Task<string> DownloadUpdateAsync(RemoteUpdateInfo updateInfo)
    {
      string updateFolder = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "BugCapture",
          "updates");

      if (!Directory.Exists(updateFolder))
      {
        Directory.CreateDirectory(updateFolder);
      }

      string fileName;
      try
      {
        var uri = new Uri(updateInfo.DownloadUrl);
        fileName = Path.GetFileName(uri.LocalPath);
      }
      catch
      {
        fileName = string.Empty;
      }

      if (string.IsNullOrWhiteSpace(fileName))
      {
        fileName = $"BugCapture-{updateInfo.LatestVersion}.exe";
      }

      string localPath = Path.Combine(updateFolder, fileName);
      using (var stream = await _httpClient.GetStreamAsync(updateInfo.DownloadUrl))
      using (var fileStream = File.Create(localPath))
      {
        await stream.CopyToAsync(fileStream);
      }

      if (!string.IsNullOrWhiteSpace(updateInfo.Sha256))
      {
        var actualHash = ComputeSha256(localPath);
        if (!string.Equals(actualHash, updateInfo.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
          File.Delete(localPath);
          throw new InvalidOperationException("Tệp cập nhật không hợp lệ (SHA256 mismatch).");
        }
      }

      return localPath;
    }

    public void LaunchInstaller(string installerPath)
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = installerPath,
        UseShellExecute = true
      });
    }

    private static bool TryParseVersion(string input, out Version version)
    {
      version = new Version(0, 0, 0, 0);
      if (string.IsNullOrWhiteSpace(input))
      {
        return false;
      }

      var normalized = input.Trim();
      if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
      {
        normalized = normalized.Substring(1);
      }

      if (!Version.TryParse(normalized, out var parsedVersion) || parsedVersion == null)
      {
        return false;
      }

      version = parsedVersion;
      return true;
    }

    private static string ComputeSha256(string filePath)
    {
      using var sha = SHA256.Create();
      using var stream = File.OpenRead(filePath);
      var hash = sha.ComputeHash(stream);
      return Convert.ToHexString(hash);
    }

    private sealed class UpdateFeedManifest
    {
      public string Version { get; set; } = string.Empty;
      public string DownloadUrl { get; set; } = string.Empty;
      public string? Notes { get; set; }
      public string? Sha256 { get; set; }
    }
  }
}
