using System;

namespace BugCapture.Models
{
    public class AppSettings
    {
        public const string DefaultAutoUpdateFeedUrl = "http://172.16.6.102:8081/update-feed.json";

        public string RedmineUrl { get; set; } = "http://localhost:3000/";
        public string RedmineApiKey { get; set; } = string.Empty;
        public string MattermostServerUrl { get; set; } = string.Empty;
        public string MattermostAccessToken { get; set; } = string.Empty;
        public bool MattermostSendToChannels { get; set; } = true;
        public bool MattermostMentionUsers { get; set; }
        public bool MattermostReplyToThreadId { get; set; }
        public string MattermostSelectedChannelReference { get; set; } = string.Empty;
        public string MattermostMentionText { get; set; } = string.Empty;
        public string MattermostThreadId { get; set; } = string.Empty;
        public bool AutoUpdateEnabled { get; set; } = true;
        public string AutoUpdateFeedUrl { get; set; } = DefaultAutoUpdateFeedUrl;
        public bool OpenEditorAfterCapture { get; set; }
        public bool IsDarkMode { get; set; } = true;

        // Last selections
        public int? LastProjectId { get; set; }
        public int? LastTrackerId { get; set; }
        public int? LastAssigneeId { get; set; }
        public string LastIssuePrefix { get; set; } = string.Empty;

        // Field values stored by TrackerId -> FieldId -> Value
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> TrackerCustomFields { get; set; } = new();

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(RedmineUrl) && !string.IsNullOrWhiteSpace(RedmineApiKey);
        }
    }
}
