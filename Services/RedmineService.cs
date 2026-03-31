using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
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
        private string _baseUrl = string.Empty;
        private string _apiKey = string.Empty;
        private readonly Logger _logger;

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
                var projects = await _manager.GetPaginatedObjectsAsync<Project>(new NameValueCollection());
                int projectsCount = 0;
                if (projects != null && projects.Items != null) projectsCount = projects.Items.Count();
                _logger.WriteLine($"Redmine: Found {projectsCount} projects.");
                return projects != null && projects.Items != null ? projects.Items.ToList() : new List<Project>();
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
                    // Project.Trackers is List<ProjectTracker> - return as-is
                    return project.Trackers.ToList();
                }

                _logger.WriteLine("Redmine: No project-specific trackers found, falling back to global.");
                var trackers = await _manager.GetPaginatedObjectsAsync<ProjectTracker>(new NameValueCollection());
                return trackers != null && trackers.Items != null ? trackers.Items.ToList() : new List<ProjectTracker>();
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetTrackersForProject)");
                var trackers = await _manager.GetPaginatedObjectsAsync<ProjectTracker>(new NameValueCollection());
                return trackers != null && trackers.Items != null ? trackers.Items.ToList() : new List<ProjectTracker>();
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
            try
            {
                _logger.WriteLine("Redmine: Fetching custom fields...");
                var fields = await _manager.GetObjectsAsync<CustomField>(new NameValueCollection());
                _logger.WriteLine($"Redmine: Found {fields?.Count ?? 0} custom fields.");
                return fields?.ToList() ?? new List<CustomField>();
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (GetCustomFields)");
                return new List<CustomField>();
            }
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

        public async Task<bool> CreateIssueAsync(Issue issue)
        {
            if (_manager == null) return false;
            try
            {
                _logger.WriteLine($"Redmine: Creating issue '{issue.Subject}'...");
                var createdIssue = await _manager.CreateObjectAsync<Issue>(issue);

                if (createdIssue != null)
                {
                    _logger.WriteLine($"Redmine: Issue #{createdIssue.Id} created successfully.");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, "Redmine Error (CreateIssue)");
                return false;
            }
        }
    }
}
