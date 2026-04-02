using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Redmine.Net.Api.Async;
using System.Threading.Tasks;
using Redmine.Net.Api;
using Redmine.Net.Api.Types;
using ShareX.HelpersLib;
using Redmine.Net.Api.Extensions;

namespace BugCapture.Services
{
    public class RedmineService
    {
        private RedmineManager? _manager;
        private static readonly TimeSpan CustomFieldsCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ForbiddenRetryInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan InferredCustomFieldsCacheDuration = TimeSpan.FromMinutes(5);
        private const int InferenceIssueSampleLimit = 100;
        private const int ListInferenceDistinctThreshold = 20;
        private const int ProjectsPageSize = 100;
        private const string BugTrackerName = "Bug";
        private string _baseUrl = string.Empty;
        private string _apiKey = string.Empty;
        private readonly Logger _logger;
        private List<CustomField>? _customFieldsCache;
        private DateTime _customFieldsCacheAtUtc = DateTime.MinValue;
        private DateTime _customFieldsForbiddenUntilUtc = DateTime.MinValue;
        private readonly Dictionary<string, (DateTime cachedAtUtc, List<CustomField> fields)> _inferredCustomFieldsCache = new();
        private readonly Dictionary<string, (DateTime cachedAtUtc, List<CustomField> fields)> _htmlCustomFieldsCache = new();
        private const string FixedAttributesFileName = "attributes.html";
        private const int MaxParentTraversal = 6;

        public RedmineService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string logDir = Path.Combine(appData, "BugCapture", "logs");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "redmine_service.log");

            _logger = new Logger(logPath);
            _logger.AsyncWrite = false;
            _logger.WriteLine($"--- Redmine Service (Library 4.65) Initialized at {DateTime.Now} ---");
        }

        public void Initialize(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/') + "/";
            _apiKey = apiKey;
            _customFieldsCache = null;
            _customFieldsCacheAtUtc = DateTime.MinValue;
            _customFieldsForbiddenUntilUtc = DateTime.MinValue;
            _inferredCustomFieldsCache.Clear();
            _htmlCustomFieldsCache.Clear();

            // Use the simple constructor for compatibility (constructor is obsolete but available)
            _manager = new RedmineManager(_baseUrl, _apiKey);
            _logger.WriteLine($"Redmine Manager (4.65) initialized for {_baseUrl}");
        }

        public async Task<List<Project>> GetProjectsAsync()
        {
            if (_manager == null) return new List<Project>();
            try
            {
                _logger.WriteLine("Redmine: Fetching projects...");
                var allProjects = new List<Project>();
                int offset = 0;

                while (true)
                {
                    var parameters = new NameValueCollection
                    {
                        { "offset", offset.ToString() },
                        { "limit", ProjectsPageSize.ToString() }
                    };

                    var page = await _manager.GetPaginatedObjectsAsync<Project>(parameters);
                    var items = page?.Items?.ToList() ?? new List<Project>();
                    if (items.Count == 0)
                    {
                        break;
                    }

                    allProjects.AddRange(items);
                    offset += items.Count;

                    if (items.Count < ProjectsPageSize)
                    {
                        break;
                    }
                }

                _logger.WriteLine($"Redmine: Found {allProjects.Count} projects.");
                return allProjects;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetProjects)");
                return new List<Project>();
            }
        }

        public async Task<List<ProjectTracker>> GetTrackersForProjectAsync(string projectIdentifier)
        {
            if (_manager == null) return new List<ProjectTracker>();
            try
            {
                _logger.WriteLine($"Redmine: Fetching trackers for project '{projectIdentifier}'...");

                var project = await _manager.GetObjectAsync<Project>(projectIdentifier, new NameValueCollection { { "include", "trackers" } });

                if (project?.Trackers != null && project.Trackers.Count > 0)
                {
                    _logger.WriteLine($"Redmine: Found {project.Trackers.Count} enabled trackers for project.");
                    return project.Trackers.ToList();
                }

                _logger.WriteLine("Redmine: No project-specific trackers found.");
                return new List<ProjectTracker>();
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetTrackersForProject)");
                return new List<ProjectTracker>();
            }
        }

        public async Task<List<IssueStatus>> GetStatusesAsync()
        {
            if (_manager == null) return new List<IssueStatus>();
            try
            {
                _logger.WriteLine("Redmine: Fetching statuses...");
                var statuses = await _manager.GetObjectsAsync<IssueStatus>(new NameValueCollection());
                return statuses?.ToList() ?? new List<IssueStatus>();
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetStatuses)");
                return new List<IssueStatus>();
            }
        }

        public async Task<List<CustomField>> GetCustomFieldsAsync()
        {
            if (_manager == null) return new List<CustomField>();

            if (_customFieldsCache != null && DateTime.UtcNow - _customFieldsCacheAtUtc < CustomFieldsCacheDuration)
            {
                return _customFieldsCache.ToList();
            }

            if (DateTime.UtcNow < _customFieldsForbiddenUntilUtc)
            {
                _logger.WriteLine("Redmine: Skipping custom fields fetch due to previous 403 response.");
                return new List<CustomField>();
            }

            try
            {
                _logger.WriteLine("Redmine: Fetching custom fields...");
                var fields = await _manager.GetObjectsAsync<CustomField>(new NameValueCollection());
                _logger.WriteLine($"Redmine: Found {fields?.Count ?? 0} custom fields.");
                _customFieldsCache = fields?.ToList() ?? new List<CustomField>();
                _customFieldsCacheAtUtc = DateTime.UtcNow;
                return _customFieldsCache.ToList();
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetCustomFields)");

                if (IsForbiddenException(ex))
                {
                    _customFieldsForbiddenUntilUtc = DateTime.UtcNow.Add(ForbiddenRetryInterval);
                    _logger.WriteLine($"Redmine: Custom fields access forbidden. Retry after {_customFieldsForbiddenUntilUtc:O}");
                }

                return new List<CustomField>();
            }
        }

        public async Task<bool> HasApiCustomFieldsAsync()
        {
            var fields = await GetCustomFieldsAsync();
            return fields.Count > 0;
        }

        public async Task<List<CustomField>> GetCustomFieldsForTrackerAsync(string projectIdentifier, int trackerId)
        {
            var allFields = await GetCustomFieldsAsync();
            if (allFields.Count > 0)
            {
                var filteredFields = FilterCustomFieldsByTracker(allFields, trackerId);
                if (filteredFields.Count > 0)
                {
                    return filteredFields;
                }
            }

            var htmlFields = await GetCustomFieldsFromIssueFormHtmlAsync(projectIdentifier, trackerId);
            if (htmlFields.Count > 0)
            {
                return htmlFields;
            }

            return new List<CustomField>();
        }

        private async Task<List<CustomField>> GetCustomFieldsFromIssueFormHtmlAsync(string projectIdentifier, int trackerId)
        {
            int? bugTrackerId = await ResolveBugTrackerIdForProjectAsync(projectIdentifier);
            if (!bugTrackerId.HasValue)
            {
                _logger.WriteLine($"Redmine: Project '{projectIdentifier}' has no '{BugTrackerName}' tracker. Skip fixed attributes HTML.");
                return new List<CustomField>();
            }

            string cacheKey = $"{projectIdentifier}:{bugTrackerId.Value}";
            if (_htmlCustomFieldsCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.cachedAtUtc < InferredCustomFieldsCacheDuration)
            {
                return cached.fields.ToList();
            }

            try
            {
                string html = await LoadFixedAttributesHtmlAsync();
                var (parsed, diagnostics) = ParseCustomFieldsFromAttributesHtml(html);
                _logger.WriteLine($"Redmine HTML Parse (fixed file): {diagnostics}");

                if (parsed.Count > 0)
                {
                    _logger.WriteLine($"Redmine: Parsed {parsed.Count} custom fields from fixed attributes HTML (tracker='{BugTrackerName}').");
                    _htmlCustomFieldsCache[cacheKey] = (DateTime.UtcNow, parsed);
                }
                else
                {
                    _logger.WriteLine($"Redmine: Could not parse any custom fields from fixed attributes HTML (tracker='{BugTrackerName}').");
                }

                return parsed;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetCustomFieldsFromIssueFormHtml)");
                return new List<CustomField>();
            }
        }

        private async Task<int?> ResolveBugTrackerIdForProjectAsync(string projectIdentifier)
        {
            var trackers = await GetTrackersForProjectAsync(projectIdentifier);
            var bugTracker = trackers.FirstOrDefault(tracker =>
                string.Equals(tracker.Name?.Trim(), BugTrackerName, StringComparison.OrdinalIgnoreCase));

            return bugTracker?.Id;
        }

        private async Task<string> LoadFixedAttributesHtmlAsync()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, FixedAttributesFileName),
                Path.Combine(Environment.CurrentDirectory, FixedAttributesFileName)
            };

            string current = AppContext.BaseDirectory;
            for (int i = 0; i < MaxParentTraversal; i++)
            {
                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
                candidates.Add(Path.Combine(current, FixedAttributesFileName));
            }

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (System.IO.File.Exists(path))
                {
                    _logger.WriteLine($"Redmine: Loading fixed attributes HTML from '{path}'.");
                    return await System.IO.File.ReadAllTextAsync(path);
                }
            }

            throw new FileNotFoundException($"Fixed HTML file not found: {FixedAttributesFileName}");
        }

        private async Task<string> FetchIssueFormHtmlAsync(string projectIdentifier, int trackerId)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.Add("X-Redmine-API-Key", _apiKey);

            string encodedProject = Uri.EscapeDataString(projectIdentifier);
            string encodedTracker = Uri.EscapeDataString(trackerId.ToString());
            string encodedKey = Uri.EscapeDataString(_apiKey);
            string url = $"{_baseUrl}issues/new?project_id={encodedProject}&tracker_id={encodedTracker}&key={encodedKey}";
            string safeUrl = $"{_baseUrl}issues/new?project_id={encodedProject}&tracker_id={encodedTracker}&key=***";

            _logger.WriteLine($"Redmine: Fetching issue form HTML for project '{projectIdentifier}' tracker '{trackerId}'.");
            _logger.WriteLine($"Redmine: HTML source URL (new issue) = {safeUrl}");
            string html = await client.GetStringAsync(url);
            _logger.WriteLine($"Redmine: HTML source length (new issue) = {html.Length}");
            return html;
        }

        private async Task<string> FetchIssueAttributesHtmlFromJsAsync(string projectIdentifier, int trackerId)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/javascript"));
            client.DefaultRequestHeaders.Add("X-Redmine-API-Key", _apiKey);

            string encodedProject = Uri.EscapeDataString(projectIdentifier);
            string encodedTracker = Uri.EscapeDataString(trackerId.ToString());
            string encodedKey = Uri.EscapeDataString(_apiKey);
            string url = $"{_baseUrl}issues/new.js?issue%5Bproject_id%5D={encodedProject}&issue%5Btracker_id%5D={encodedTracker}&key={encodedKey}";
            string safeUrl = $"{_baseUrl}issues/new.js?issue%5Bproject_id%5D={encodedProject}&issue%5Btracker_id%5D={encodedTracker}&key=***";

            _logger.WriteLine($"Redmine: Fetching issue attributes JS for project '{projectIdentifier}' tracker '{trackerId}'.");
            _logger.WriteLine($"Redmine: HTML source URL (new.js) = {safeUrl}");

            string js = await client.GetStringAsync(url);
            _logger.WriteLine($"Redmine: JS source length (new.js) = {js.Length}");

            string html = ExtractHtmlFromAttributesJs(js);
            _logger.WriteLine($"Redmine: Extracted HTML length from new.js = {html.Length}");
            return html;
        }

        private async Task<string> FetchIssueEditFormHtmlAsync(string projectIdentifier, int trackerId)
        {
            if (_manager == null)
            {
                return string.Empty;
            }

            var parameters = new NameValueCollection
            {
                { "project_id", projectIdentifier },
                { "tracker_id", trackerId.ToString() },
                { "limit", "1" },
                { "sort", "updated_on:desc" }
            };

            var issues = await _manager.GetPaginatedObjectsAsync<Issue>(parameters);
            var latestIssue = issues?.Items?.FirstOrDefault();
            if (latestIssue == null || latestIssue.Id <= 0)
            {
                return string.Empty;
            }

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.Add("X-Redmine-API-Key", _apiKey);

            string issueId = Uri.EscapeDataString(latestIssue.Id.ToString());
            string encodedKey = Uri.EscapeDataString(_apiKey);
            string url = $"{_baseUrl}issues/{issueId}/edit?key={encodedKey}";
            string safeUrl = $"{_baseUrl}issues/{issueId}/edit?key=***";

            _logger.WriteLine($"Redmine: Fetching edit issue HTML for issue '{latestIssue.Id}' as fallback.");
            _logger.WriteLine($"Redmine: HTML source URL (edit issue) = {safeUrl}");
            string html = await client.GetStringAsync(url);
            _logger.WriteLine($"Redmine: HTML source length (edit issue) = {html.Length}");
            return html;
        }

        private static string ExtractHtmlFromAttributesJs(string js)
        {
            if (string.IsNullOrWhiteSpace(js))
            {
                return string.Empty;
            }

            var match = Regex.Match(js, "\\.html\\((['\\\"])(?<payload>(?:\\\\.|(?!\\1).)*)\\1\\)", RegexOptions.Singleline);
            if (!match.Success)
            {
                return string.Empty;
            }

            string payload = match.Groups["payload"].Value;
            try
            {
                return JsonConvert.DeserializeObject<string>($"\"{payload.Replace("\"", "\\\"")}\"") ?? string.Empty;
            }
            catch
            {
                return Regex.Unescape(payload);
            }
        }

        private static (List<CustomField> fields, string diagnostics) ParseCustomFieldsFromAttributesHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return (new List<CustomField>(), "empty-html");
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var primaryNodes = doc.DocumentNode.SelectNodes("//*[@name and contains(@name, 'issue[custom_field_values][')]");
            IEnumerable<HtmlNode> nodes = primaryNodes ?? Enumerable.Empty<HtmlNode>();
            int primaryCount = primaryNodes?.Count ?? 0;
            int controlCount = 0;

            if (!nodes.Any())
            {
                var controlNodes = doc.DocumentNode.SelectNodes("//*[@name and (self::select or self::textarea or self::input)]");
                controlCount = controlNodes?.Count ?? 0;
                nodes = (controlNodes != null ? controlNodes.AsEnumerable() : Enumerable.Empty<HtmlNode>())
                    .Where(node => ExtractCustomFieldId(node.GetAttributeValue("name", string.Empty)).HasValue);
            }

            var matchedNodes = nodes.ToList();
            if (!matchedNodes.Any())
            {
                return (new List<CustomField>(), $"html-len={html.Length}; primary={primaryCount}; controls={controlCount}; matched=0");
            }

            var nodesByFieldId = matchedNodes
                .Select(node => new
                {
                    Node = node,
                    Id = ExtractCustomFieldId(node.GetAttributeValue("name", string.Empty))
                })
                .Where(x => x.Id.HasValue && x.Id.Value > 0)
                .GroupBy(x => x.Id!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Select(x => x.Node)
                        .OrderByDescending(GetNodePriority)
                        .First()
                );

            var fields = new List<CustomField>();
            int skippedTextAreas = 0;

            foreach (var fieldNode in nodesByFieldId)
            {
                int id = fieldNode.Key;
                var node = fieldNode.Value;

                if (node.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase))
                {
                    skippedTextAreas++;
                    continue;
                }

                string inputId = node.GetAttributeValue("id", string.Empty);
                string label = FindLabelText(doc, inputId);
                bool isRequired = IsRequiredField(doc, inputId);
                string fieldFormat = DetermineFieldFormat(node);
                string? defaultValue = ExtractDefaultValue(node, fieldFormat);

                var fieldJson = new JObject
                {
                    ["id"] = id,
                    ["name"] = string.IsNullOrWhiteSpace(label) ? $"CF_{id}" : label,
                    ["field_format"] = fieldFormat,
                    ["is_required"] = isRequired
                };

                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    fieldJson["default_value"] = defaultValue;
                }

                if (fieldFormat == "list")
                {
                    var options = ParseSelectOptions(node);
                    if (options.Count > 0)
                    {
                        fieldJson["possible_values"] = new JArray(options.Select(o => new JObject
                        {
                            ["label"] = o.label,
                            ["value"] = o.value
                        }));
                    }
                }

                var customField = new CustomField();
                using var reader = new JsonTextReader(new StringReader(fieldJson.ToString(Formatting.None)));
                customField.ReadJson(reader);
                fields.Add(customField);
            }

            var orderedFields = fields.OrderBy(f => f.Name).ToList();
            string fieldSummary = string.Join(", ",
                orderedFields.Select(f => $"{f.Id}:{f.Name}:{f.FieldFormat}:opts={f.PossibleValues?.Count ?? 0}"));

            return (orderedFields,
                $"html-len={html.Length}; primary={primaryCount}; controls={controlCount}; matched={matchedNodes.Count}; unique={nodesByFieldId.Count}; skippedTextAreas={skippedTextAreas}; parsed={orderedFields.Count}; fields=[{fieldSummary}]");
        }

        private static int GetNodePriority(HtmlNode node)
        {
            string nodeName = node.Name.ToLowerInvariant();
            string type = node.GetAttributeValue("type", string.Empty).ToLowerInvariant();
            string className = node.GetAttributeValue("class", string.Empty);

            if (nodeName == "select") return 100;
            if (nodeName == "textarea") return 90;
            if (type == "checkbox") return 80;
            if (type == "date") return 70;
            if (type == "text") return 60;
            if (type == "hidden") return 10;
            if (className.Contains("bool_cf", StringComparison.OrdinalIgnoreCase)) return 75;

            return 50;
        }

        private static int? ExtractCustomFieldId(string name)
        {
            var match = Regex.Match(name, @"issue\[custom_field_values\]\[(\d+)\]");
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, out int id) ? id : null;
        }

        private static string FindLabelText(HtmlDocument doc, string inputId)
        {
            if (string.IsNullOrWhiteSpace(inputId))
            {
                return string.Empty;
            }

            var labelNode = doc.DocumentNode.SelectSingleNode($"//label[@for='{inputId}']");
            if (labelNode == null)
            {
                return string.Empty;
            }

            var text = HtmlEntity.DeEntitize(labelNode.InnerText ?? string.Empty);
            return Regex.Replace(text, "\\s+", " ").Trim();
        }

        private static bool IsRequiredField(HtmlDocument doc, string inputId)
        {
            if (string.IsNullOrWhiteSpace(inputId))
            {
                return false;
            }

            var labelNode = doc.DocumentNode.SelectSingleNode($"//label[@for='{inputId}']");
            return labelNode?.SelectSingleNode(".//span[contains(@class, 'required')]") != null;
        }

        private static string DetermineFieldFormat(HtmlNode node)
        {
            string className = node.GetAttributeValue("class", string.Empty);
            string nodeName = node.Name.ToLowerInvariant();
            string inputType = node.GetAttributeValue("type", string.Empty).ToLowerInvariant();

            if (nodeName == "select") return "list";
            if (nodeName == "textarea") return "text";

            if (nodeName == "input")
            {
                if (inputType == "checkbox") return "bool";
                if (inputType == "date") return "date";
                if (inputType == "number") return "float";
                if (inputType == "text" || string.IsNullOrWhiteSpace(inputType)) return "string";
            }

            if (className.Contains("bool_cf", StringComparison.OrdinalIgnoreCase)) return "bool";
            if (className.Contains("date_cf", StringComparison.OrdinalIgnoreCase)) return "date";
            if (className.Contains("int_cf", StringComparison.OrdinalIgnoreCase)) return "int";
            if (className.Contains("float_cf", StringComparison.OrdinalIgnoreCase)) return "float";
            if (className.Contains("text_cf", StringComparison.OrdinalIgnoreCase)) return "text";
            if (className.Contains("string_cf", StringComparison.OrdinalIgnoreCase)) return "string";
            if (className.Contains("enumeration_cf", StringComparison.OrdinalIgnoreCase) || className.Contains("list_cf", StringComparison.OrdinalIgnoreCase)) return "list";

            return "string";
        }

        private static string? ExtractDefaultValue(HtmlNode node, string fieldFormat)
        {
            if (fieldFormat == "bool")
            {
                return "0";
            }

            if (node.Name.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                var emptyOption = node.SelectSingleNode("./option[@value='']");
                if (emptyOption != null)
                {
                    return string.Empty;
                }

                return string.Empty;
            }

            if (node.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static List<(string label, string value)> ParseSelectOptions(HtmlNode node)
        {
            var result = new List<(string label, string value)>();
            if (!node.Name.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            var optionNodes = node.SelectNodes("./option");
            if (optionNodes == null)
            {
                return result;
            }

            foreach (var option in optionNodes)
            {
                string value = option.GetAttributeValue("value", string.Empty).Trim();
                string label = Regex.Replace(HtmlEntity.DeEntitize(option.InnerText ?? string.Empty), "\\s+", " ").Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result.Add((string.IsNullOrWhiteSpace(label) ? value : label, value));
            }

            return result;
        }

        private static List<CustomField> FilterCustomFieldsByTracker(IEnumerable<CustomField> fields, int trackerId)
        {
            return fields
                .Where(field => field.Trackers == null || field.Trackers.Count == 0 || field.Trackers.Any(t => t.Id == trackerId))
                .ToList();
        }

        private static bool IsForbiddenException(Exception ex)
        {
            Exception? current = ex;

            while (current != null)
            {
                if (current is WebException webException)
                {
                    if (webException.Response is HttpWebResponse response)
                    {
                        return response.StatusCode == HttpStatusCode.Forbidden;
                    }
                }

                current = current.InnerException;
            }

            return false;
        }

        public async Task<Upload?> UploadFileAsync(string filePath)
        {
            if (_manager == null || !System.IO.File.Exists(filePath)) return null;

            try
            {
                _logger.WriteLine($"Redmine: Uploading {filePath}...");
                byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

                var upload = await _manager.UploadFileAsync(fileBytes);
                if (upload != null)
                {
                    upload.FileName = Path.GetFileName(filePath);
                    upload.ContentType = "image/png";
                    _logger.WriteLine($"Redmine: Upload success. Token: {upload.Token}");
                    return upload;
                }
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (UploadFile)");
            }
            return null;
        }

        public async Task<List<ProjectMembership>> GetMembershipsAsync(string projectIdentifier, int projectId)
        {
            if (_manager == null) return new List<ProjectMembership>();
            try
            {
                _logger.WriteLine($"Redmine: Fetching members via GetProjectMembershipsAsync for '{projectIdentifier}'...");

                var pagedResults = await _manager.GetProjectMembershipsAsync(projectIdentifier);

                var memberships = pagedResults?.Items?.ToList() ?? new List<ProjectMembership>();

                _logger.WriteLine($"Redmine: Found {memberships.Count} members.");
                return memberships;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetMembershipsAsync)");
                return new List<ProjectMembership>();
            }
        }

        public async Task<Issue?> CreateIssueAsync(Issue issue)
        {
            if (_manager == null) return null;
            try
            {
                _logger.WriteLine($"Redmine: Creating issue '{issue.Subject}'...");
                var createdIssue = await _manager.CreateObjectAsync<Issue>(issue);

                if (createdIssue != null)
                {
                    _logger.WriteLine($"Redmine: Issue #{createdIssue.Id} created successfully.");
                    return createdIssue;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (CreateIssue)");
                return null;
            }
        }
    }
}
