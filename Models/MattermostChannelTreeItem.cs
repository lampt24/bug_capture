namespace BugCapture.Models
{
  public class MattermostChannelTreeItem
  {
    public bool IsTeamHeader { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public MattermostChannelInfo? Channel { get; set; }
  }
}
