using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BugCapture.Models;

namespace BugCapture.Services
{
  public class MattermostService
  {
    private const bool EnableMattermostActionButtons = false;

    public sealed class MattermostInteractivePostOptions
    {
      public string Pretext { get; set; } = string.Empty;
      public string Text { get; set; } = string.Empty;
      public string Assignee { get; set; } = string.Empty;
      public string Creator { get; set; } = string.Empty;
      public string CreatedAtText { get; set; } = string.Empty;
      public string RedmineUrl { get; set; } = string.Empty;
      public string IntegrationUrl { get; set; } = string.Empty;
      public int IssueId { get; set; }
      public string ThreadRootId { get; set; } = string.Empty;
    }

    private readonly HttpClient _httpClient = new HttpClient();
    private string _serverUrl = string.Empty;
    private string _accessToken = string.Empty;
    private string _currentUserId = string.Empty;
    private string _currentUsername = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_serverUrl) && !string.IsNullOrWhiteSpace(_accessToken);

    public void Initialize(string serverUrl, string accessToken)
    {
      _serverUrl = (serverUrl ?? string.Empty).Trim().TrimEnd('/');
      _accessToken = (accessToken ?? string.Empty).Trim();
      _currentUserId = string.Empty;
      _currentUsername = string.Empty;

      _httpClient.DefaultRequestHeaders.Clear();
      if (!string.IsNullOrWhiteSpace(_accessToken))
      {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
      }
    }

    public async Task<(List<MattermostChannelInfo> channels, List<MattermostUserInfo> users)> GetChannelsAndMembersAsync()
    {
      EnsureConfigured();

      var me = await GetCurrentUserAsync();
      EnsureBotAccount(me);
      _currentUserId = me.Id;
      _currentUsername = me.Username;

      var teamsById = await TryGetMyTeamsLookupAsync();
      var myChannels = await GetMyChannelsAsync();
      var channels = new Dictionary<string, MattermostChannelInfo>(StringComparer.OrdinalIgnoreCase);
      var users = new Dictionary<string, MattermostUserInfo>(StringComparer.OrdinalIgnoreCase);

      foreach (var channel in myChannels)
      {
        if (string.IsNullOrWhiteSpace(channel.Id) || string.IsNullOrWhiteSpace(channel.Name))
        {
          continue;
        }

        var channelTeamId = channel.TeamId ?? string.Empty;
        teamsById.TryGetValue(channelTeamId, out var teamInfo);
        var hasTeamInfo = !string.IsNullOrWhiteSpace(channelTeamId) && teamInfo != null;
        channels[channel.Id] = new MattermostChannelInfo
        {
          Id = channel.Id,
          TeamId = channelTeamId,
          TeamName = hasTeamInfo ? teamInfo.Name ?? string.Empty : channelTeamId,
          TeamDisplayName = hasTeamInfo
              ? (string.IsNullOrWhiteSpace(teamInfo.DisplayName) ? teamInfo.Name ?? string.Empty : teamInfo.DisplayName)
              : channelTeamId,
          Name = channel.Name,
          DisplayName = channel.DisplayName ?? string.Empty
        };
      }

      foreach (var channel in channels.Values)
      {
        int page = 0;
        while (true)
        {
          var channelUsers = await TryGetUsersByChannelAsync(channel.Id, page, 200);
          if (channelUsers == null || channelUsers.Count == 0)
          {
            break;
          }

          foreach (var user in channelUsers)
          {
            if (!string.Equals(user.Id, _currentUserId, StringComparison.OrdinalIgnoreCase))
            {
              users[user.Id] = user;
            }
          }

          page++;
        }
      }

      return (
          channels.Values.OrderBy(c => c.Name).ToList(),
          users.Values.OrderBy(u => u.Username).ToList());
    }

    public async Task<int> SendToChannelsAsync(
      IEnumerable<MattermostChannelInfo> channels,
      string message,
      IEnumerable<string> filePaths,
      MattermostInteractivePostOptions? interactiveOptions = null)
    {
      EnsureConfigured();
      int sentCount = 0;

      foreach (var channel in channels)
      {
        try
        {
          var fileIds = await UploadFilesToChannelAsync(channel.Id, filePaths);
          await CreatePostAsync(channel.Id, message, fileIds, interactiveOptions);
          sentCount++;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains(" 403", StringComparison.Ordinal) || ex.Message.Contains("status_code\":403", StringComparison.OrdinalIgnoreCase))
        {
          var teamLabel = string.IsNullOrWhiteSpace(channel.TeamDisplayName)
              ? (string.IsNullOrWhiteSpace(channel.TeamName) ? channel.TeamId : channel.TeamName)
              : channel.TeamDisplayName;

          throw new HttpRequestException(
              $"Mattermost 403 khi gửi vào channel {channel.Reference} (team: {teamLabel}, channelId: {channel.Id}). Token hợp lệ nhưng tài khoản/token chưa có quyền post tại channel này (thường do role/scheme hoặc channel bị read-only).",
              ex);
        }
      }

      return sentCount;
    }

    public async Task<int> SendDirectToUsersAsync(
      IEnumerable<MattermostUserInfo> users,
      string message,
      IEnumerable<string> filePaths,
      MattermostInteractivePostOptions? interactiveOptions = null)
    {
      EnsureConfigured();
      await EnsureCurrentUserIdAsync();

      int sentCount = 0;
      foreach (var user in users)
      {
        var directChannelId = await CreateDirectChannelAsync(user.Id);
        var fileIds = await UploadFilesToChannelAsync(directChannelId, filePaths);
        await CreatePostAsync(directChannelId, message, fileIds, interactiveOptions);
        sentCount++;
      }

      return sentCount;
    }

    public async Task SendToThreadAsync(
      string postOrThreadId,
      string message,
      IEnumerable<string> filePaths,
      MattermostInteractivePostOptions? interactiveOptions = null)
    {
      EnsureConfigured();

      var post = await GetPostAsync(postOrThreadId);
      var channelId = post.ChannelId;
      var threadRootId = string.IsNullOrWhiteSpace(post.RootId) ? post.Id : post.RootId;
      if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(threadRootId))
      {
        throw new InvalidOperationException("Cannot resolve channel/thread from Mattermost post id.");
      }

      var options = interactiveOptions ?? new MattermostInteractivePostOptions();
      options.ThreadRootId = threadRootId;

      var fileIds = await UploadFilesToChannelAsync(channelId, filePaths);
      await CreatePostAsync(channelId, message, fileIds, options);
    }

    public async Task<string> GetCurrentUserMentionAsync()
    {
      await EnsureCurrentUserIdAsync();
      if (string.IsNullOrWhiteSpace(_currentUsername))
      {
        return string.Empty;
      }

      return "@" + _currentUsername;
    }

    public async Task<string> GetCurrentUsernameAsync()
    {
      await EnsureCurrentUserIdAsync();
      return _currentUsername ?? string.Empty;
    }

    public async Task<List<MattermostUserInfo>> GetChannelMembersAsync(string channelId)
    {
      EnsureConfigured();

      if (string.IsNullOrWhiteSpace(channelId))
      {
        return new List<MattermostUserInfo>();
      }

      var users = new Dictionary<string, MattermostUserInfo>(StringComparer.OrdinalIgnoreCase);
      int page = 0;

      while (true)
      {
        var pageUsers = await TryGetUsersByChannelAsync(channelId.Trim(), page, 200);
        if (pageUsers == null || pageUsers.Count == 0)
        {
          break;
        }

        foreach (var user in pageUsers)
        {
          if (!string.IsNullOrWhiteSpace(user.Id))
          {
            users[user.Id] = user;
          }
        }

        page++;
      }

      return users.Values.OrderBy(u => u.Username).ToList();
    }

    private async Task EnsureCurrentUserIdAsync()
    {
      if (!string.IsNullOrWhiteSpace(_currentUserId))
      {
        return;
      }

      var me = await GetCurrentUserAsync();
      EnsureBotAccount(me);
      _currentUserId = me.Id;
      _currentUsername = me.Username;
    }

    private static void EnsureBotAccount(MattermostUserDto user)
    {
      if (!user.IsBot)
      {
        throw new InvalidOperationException("Mattermost token hiện tại không thuộc BOT account. Vui lòng dùng BOT access token để gửi tin nhắn.");
      }
    }

    private void EnsureConfigured()
    {
      if (!IsConfigured)
      {
        throw new InvalidOperationException("Mattermost is not configured.");
      }
    }

    private async Task<MattermostUserDto> GetCurrentUserAsync()
    {
      using var response = await _httpClient.GetAsync(BuildApiUrl("/api/v4/users/me"));
      await EnsureSuccessAsync(response);
      return await DeserializeAsync<MattermostUserDto>(response);
    }

    private async Task<MattermostPostDto> GetPostAsync(string postId)
    {
      if (string.IsNullOrWhiteSpace(postId))
      {
        throw new ArgumentException("Post id is required.", nameof(postId));
      }

      using var response = await _httpClient.GetAsync(BuildApiUrl($"/api/v4/posts/{postId.Trim()}"));
      await EnsureSuccessAsync(response);
      return await DeserializeAsync<MattermostPostDto>(response);
    }

    private async Task<List<MattermostTeamDto>> GetMyTeamsAsync()
    {
      using var response = await _httpClient.GetAsync(BuildApiUrl("/api/v4/users/me/teams"));
      await EnsureSuccessAsync(response);
      return await DeserializeAsync<List<MattermostTeamDto>>(response);
    }

    private async Task<Dictionary<string, MattermostTeamDto>> TryGetMyTeamsLookupAsync()
    {
      try
      {
        var teams = await GetMyTeamsAsync();
        return teams
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
      }
      catch (HttpRequestException ex) when (IsForbidden(ex))
      {
        return new Dictionary<string, MattermostTeamDto>(StringComparer.OrdinalIgnoreCase);
      }
    }

    private async Task<List<MattermostChannelDto>> GetMyChannelsAsync()
    {
      using var response = await _httpClient.GetAsync(BuildApiUrl("/api/v4/users/me/channels"));
      await EnsureSuccessAsync(response);
      return await DeserializeAsync<List<MattermostChannelDto>>(response);
    }

    private async Task<List<MattermostUserInfo>?> TryGetUsersByChannelAsync(string channelId, int page, int perPage)
    {
      try
      {
        using var response = await _httpClient.GetAsync(BuildApiUrl($"/api/v4/users?in_channel={channelId}&page={page}&per_page={perPage}"));
        await EnsureSuccessAsync(response);
        var users = await DeserializeAsync<List<MattermostUserDto>>(response);

        return users
            .Where(u => !string.IsNullOrWhiteSpace(u.Id) && !string.IsNullOrWhiteSpace(u.Username))
            .Select(u => new MattermostUserInfo
            {
              Id = u.Id,
              Username = u.Username,
              FirstName = u.FirstName ?? string.Empty,
              LastName = u.LastName ?? string.Empty,
              Nickname = u.Nickname ?? string.Empty
            })
            .ToList();
      }
      catch (HttpRequestException ex) when (IsForbidden(ex))
      {
        return null;
      }
    }

    private static bool IsForbidden(HttpRequestException ex)
    {
      var message = ex.Message ?? string.Empty;
      return message.Contains(" 403", StringComparison.Ordinal)
          || message.Contains("error 403", StringComparison.OrdinalIgnoreCase)
          || message.Contains("status_code\":403", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<string>> UploadFilesToChannelAsync(string channelId, IEnumerable<string> filePaths)
    {
      var result = new List<string>();

      foreach (var path in filePaths.Where(File.Exists))
      {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(channelId), "channel_id");

        var streamContent = new StreamContent(File.OpenRead(path));
        multipart.Add(streamContent, "files", Path.GetFileName(path));

        using var response = await _httpClient.PostAsync(BuildApiUrl("/api/v4/files"), multipart);
        await EnsureSuccessAsync(response);
        var uploadResult = await DeserializeAsync<MattermostFileUploadResponseDto>(response);
        if (uploadResult.FileInfos != null)
        {
          result.AddRange(uploadResult.FileInfos
              .Where(f => !string.IsNullOrWhiteSpace(f.Id))
              .Select(f => f.Id));
        }
      }

      return result;
    }

    private async Task<string> CreateDirectChannelAsync(string targetUserId)
    {
      await EnsureCurrentUserIdAsync();

      var payload = JsonSerializer.Serialize(new[] { _currentUserId, targetUserId });
      using var response = await _httpClient.PostAsync(
          BuildApiUrl("/api/v4/channels/direct"),
          new StringContent(payload, Encoding.UTF8, "application/json"));

      await EnsureSuccessAsync(response);
      var channel = await DeserializeAsync<MattermostChannelDto>(response);
      return channel.Id;
    }

    private async Task CreatePostAsync(string channelId, string message, List<string> fileIds, MattermostInteractivePostOptions? interactiveOptions)
    {
      var payloadObj = new MattermostCreatePostDto
      {
        ChannelId = channelId,
        Message = interactiveOptions == null ? message : string.Empty,
        FileIds = fileIds,
        Props = BuildInteractiveProps(interactiveOptions),
        RootId = string.IsNullOrWhiteSpace(interactiveOptions?.ThreadRootId) ? null : interactiveOptions!.ThreadRootId
      };

      var payload = JsonSerializer.Serialize(payloadObj);
      using var response = await _httpClient.PostAsync(
          BuildApiUrl("/api/v4/posts"),
          new StringContent(payload, Encoding.UTF8, "application/json"));

      await EnsureSuccessAsync(response);
    }

    private static MattermostPostPropsDto? BuildInteractiveProps(MattermostInteractivePostOptions? options)
    {
      if (options == null)
      {
        return null;
      }

      var fields = new List<MattermostAttachmentFieldDto>();

      if (!string.IsNullOrWhiteSpace(options.RedmineUrl))
      {
        fields.Add(new MattermostAttachmentFieldDto { Title = "Redmine", Value = options.RedmineUrl, Short = false });
      }

      var actions = new List<MattermostAttachmentActionDto>();
      if (EnableMattermostActionButtons)
      {
        var integrationUrl = string.IsNullOrWhiteSpace(options.IntegrationUrl) ? "http://localhost" : options.IntegrationUrl.Trim();
        actions.Add(CreateStatusAction("Đang fix", "in_progress", integrationUrl, options.IssueId));
        actions.Add(CreateStatusAction("Đã Fix", "fixed", integrationUrl, options.IssueId));
        actions.Add(CreateStatusAction("Đã Confirm", "confirmed", integrationUrl, options.IssueId));
      }

      return new MattermostPostPropsDto
      {
        Attachments = new List<MattermostAttachmentDto>
        {
          new MattermostAttachmentDto
          {
            Pretext = string.Empty,
            Color = "#D32F2F",
            Title = options.Pretext,
            Text = options.Text,
            Fields = fields,
            Actions = actions
          }
        }
      };
    }

    private static MattermostAttachmentActionDto CreateStatusAction(string name, string status, string integrationUrl, int issueId)
    {
      return new MattermostAttachmentActionDto
      {
        Name = name,
        Integration = new MattermostActionIntegrationDto
        {
          Url = integrationUrl,
          Context = new MattermostActionContextDto
          {
            Status = status,
            IssueId = issueId
          }
        }
      };
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
    {
      var content = await response.Content.ReadAsStringAsync();
      var obj = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (obj == null)
      {
        throw new InvalidOperationException("Failed to parse Mattermost response.");
      }

      return obj;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
      if (response.IsSuccessStatusCode)
      {
        return;
      }

      var body = await response.Content.ReadAsStringAsync();
      throw new HttpRequestException($"Mattermost API error {(int)response.StatusCode}: {body}");
    }

    private string BuildApiUrl(string path)
    {
      return _serverUrl + path;
    }

    private class MattermostTeamDto
    {
      public string Id { get; set; } = string.Empty;
      public string? Name { get; set; }
      [JsonPropertyName("display_name")]
      public string? DisplayName { get; set; }
    }

    private class MattermostChannelDto
    {
      public string Id { get; set; } = string.Empty;
      [JsonPropertyName("team_id")]
      public string? TeamId { get; set; }
      public string Name { get; set; } = string.Empty;
      [JsonPropertyName("display_name")]
      public string? DisplayName { get; set; }
    }

    private class MattermostUserDto
    {
      public string Id { get; set; } = string.Empty;
      public string Username { get; set; } = string.Empty;
      [JsonPropertyName("is_bot")]
      public bool IsBot { get; set; }
      [JsonPropertyName("first_name")]
      public string? FirstName { get; set; }
      [JsonPropertyName("last_name")]
      public string? LastName { get; set; }
      public string? Nickname { get; set; }
    }

    private class MattermostFileUploadResponseDto
    {
      [JsonPropertyName("file_infos")]
      public List<MattermostFileInfoDto> FileInfos { get; set; } = new List<MattermostFileInfoDto>();
    }

    private class MattermostFileInfoDto
    {
      public string Id { get; set; } = string.Empty;
    }

    private class MattermostCreatePostDto
    {
      [JsonPropertyName("channel_id")]
      public string ChannelId { get; set; } = string.Empty;

      [JsonPropertyName("message")]
      public string Message { get; set; } = string.Empty;

      [JsonPropertyName("file_ids")]
      public List<string> FileIds { get; set; } = new List<string>();

      [JsonPropertyName("props")]
      [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
      public MattermostPostPropsDto? Props { get; set; }

      [JsonPropertyName("root_id")]
      [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
      public string? RootId { get; set; }
    }

    private class MattermostPostDto
    {
      public string Id { get; set; } = string.Empty;

      [JsonPropertyName("channel_id")]
      public string ChannelId { get; set; } = string.Empty;

      [JsonPropertyName("root_id")]
      public string? RootId { get; set; }
    }

    private class MattermostPostPropsDto
    {
      [JsonPropertyName("attachments")]
      public List<MattermostAttachmentDto> Attachments { get; set; } = new List<MattermostAttachmentDto>();
    }

    private class MattermostAttachmentDto
    {
      [JsonPropertyName("pretext")]
      public string Pretext { get; set; } = string.Empty;

      [JsonPropertyName("color")]
      public string Color { get; set; } = string.Empty;

      [JsonPropertyName("title")]
      public string Title { get; set; } = string.Empty;

      [JsonPropertyName("text")]
      public string Text { get; set; } = string.Empty;

      [JsonPropertyName("fields")]
      public List<MattermostAttachmentFieldDto> Fields { get; set; } = new List<MattermostAttachmentFieldDto>();

      [JsonPropertyName("actions")]
      public List<MattermostAttachmentActionDto> Actions { get; set; } = new List<MattermostAttachmentActionDto>();
    }

    private class MattermostAttachmentFieldDto
    {
      [JsonPropertyName("title")]
      public string Title { get; set; } = string.Empty;

      [JsonPropertyName("value")]
      public string Value { get; set; } = string.Empty;

      [JsonPropertyName("short")]
      public bool Short { get; set; }
    }

    private class MattermostAttachmentActionDto
    {
      [JsonPropertyName("name")]
      public string Name { get; set; } = string.Empty;

      [JsonPropertyName("integration")]
      public MattermostActionIntegrationDto Integration { get; set; } = new MattermostActionIntegrationDto();
    }

    private class MattermostActionIntegrationDto
    {
      [JsonPropertyName("url")]
      public string Url { get; set; } = string.Empty;

      [JsonPropertyName("context")]
      public MattermostActionContextDto Context { get; set; } = new MattermostActionContextDto();
    }

    private class MattermostActionContextDto
    {
      [JsonPropertyName("status")]
      public string Status { get; set; } = string.Empty;

      [JsonPropertyName("issue_id")]
      public int IssueId { get; set; }
    }
  }
}
