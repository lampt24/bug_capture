using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BugCapture.Models
{
    public class RedmineProject
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("identifier")]
        public string Identifier { get; set; } = string.Empty;
    }

    public class RedmineTracker
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class RedmineCustomField
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("field_format")]
        public string FieldFormat { get; set; } = string.Empty;

        [JsonProperty("multiple")]
        public bool Multiple { get; set; }

        [JsonProperty("possible_values")]
        public List<RedmineCustomFieldValue>? PossibleValues { get; set; }

        [JsonProperty("required")]
        public bool IsRequired { get; set; }
    }

    public class RedmineCustomFieldValue
    {
        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;

        [JsonProperty("label")]
        public string Label { get; set; } = string.Empty;

        public override string ToString() => Label;
    }

    public class RedmineIssue
    {
        [JsonProperty("project_id")]
        public int ProjectId { get; set; }

        [JsonProperty("tracker_id")]
        public int TrackerId { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("custom_fields")]
        public List<RedmineIssueCustomField>? CustomFields { get; set; }

        [JsonProperty("uploads")]
        public List<RedmineUploadToken>? Uploads { get; set; }
    }

    public class RedmineIssueCustomField
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("value")]
        public object? Value { get; set; }
    }

    public class RedmineUploadToken
    {
        [JsonProperty("token")]
        public string Token { get; set; } = string.Empty;

        [JsonProperty("filename")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("content_type")]
        public string ContentType { get; set; } = "image/png";
    }

    // API Response Wrappers
    public class RedmineProjectResponse
    {
        [JsonProperty("projects")]
        public List<RedmineProject> Projects { get; set; } = new();
    }

    public class RedmineTrackerResponse
    {
        [JsonProperty("trackers")]
        public List<RedmineTracker> Trackers { get; set; } = new();
    }

    public class RedmineCustomFieldResponse
    {
        [JsonProperty("custom_fields")]
        public List<RedmineCustomField> CustomFields { get; set; } = new();
    }
    
    public class RedmineUploadResponse
    {
        [JsonProperty("upload")]
        public RedmineUploadToken Upload { get; set; } = new();
    }

    public class RedmineIssueRequest
    {
        [JsonProperty("issue")]
        public RedmineIssue Issue { get; set; } = new();
    }
}
