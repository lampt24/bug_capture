namespace BugCapture.Models
{
  public class MattermostChannelInfo
  {
    public string Id { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string TeamDisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public string Reference => $"#{Name}";
    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName)
        ? Reference
        : $"{Reference} ({DisplayName})";
  }
}
