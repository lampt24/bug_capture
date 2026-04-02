using System;

namespace BugCapture.Models
{
  public class MattermostUserInfo
  {
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;

    public string MentionHandle => string.IsNullOrWhiteSpace(Username) ? string.Empty : $"@{Username}";

    public string DisplayText
    {
      get
      {
        var fullName = (FirstName + " " + LastName).Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
          fullName = Nickname;
        }

        return string.IsNullOrWhiteSpace(fullName)
            ? MentionHandle
            : $"{MentionHandle} ({fullName})";
      }
    }

    public bool Matches(string query)
    {
      if (string.IsNullOrWhiteSpace(query)) return false;

      return Username.Contains(query, StringComparison.OrdinalIgnoreCase)
          || FirstName.Contains(query, StringComparison.OrdinalIgnoreCase)
          || LastName.Contains(query, StringComparison.OrdinalIgnoreCase)
          || Nickname.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
  }
}
