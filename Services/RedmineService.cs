using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BugCapture.Models;
using Newtonsoft.Json;

namespace BugCapture.Services
{
    public class RedmineService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl = string.Empty;
        private string _apiKey = string.Empty;

        public RedmineService()
        {
            _httpClient = new HttpClient();
        }

        public void Initialize(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/') + "/";
            _apiKey = apiKey;
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Redmine-API-Key", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<RedmineProject>> GetProjectsAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_baseUrl}projects.json?limit=100");
                var data = JsonConvert.DeserializeObject<RedmineProjectResponse>(response);
                return data?.Projects ?? new List<RedmineProject>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redmine Error (GetProjects): {ex.Message}");
                return new List<RedmineProject>();
            }
        }

        public async Task<List<RedmineTracker>> GetTrackersAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_baseUrl}trackers.json");
                var data = JsonConvert.DeserializeObject<RedmineTrackerResponse>(response);
                return data?.Trackers ?? new List<RedmineTracker>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redmine Error (GetTrackers): {ex.Message}");
                return new List<RedmineTracker>();
            }
        }

        public async Task<List<RedmineCustomField>> GetCustomFieldsAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync($"{_baseUrl}custom_fields.json");
                
                // Redmine API for custom_fields might not include 'trackers' association by default
                // unless it is the latest version. We will fetch all and filter later.
                var data = JsonConvert.DeserializeObject<RedmineCustomFieldResponse>(response);
                return data?.CustomFields ?? new List<RedmineCustomField>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redmine Error (GetCustomFields): {ex.Message}");
                return new List<RedmineCustomField>();
            }
        }

        public async Task<RedmineUploadToken?> UploadFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                var content = new ByteArrayContent(fileBytes);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var fileName = Path.GetFileName(filePath);
                var response = await _httpClient.PostAsync($"{_baseUrl}uploads.json?filename={Uri.EscapeDataString(fileName)}", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<RedmineUploadResponse>(json);
                    if (result?.Upload != null)
                    {
                        result.Upload.FileName = fileName;
                        return result.Upload;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redmine Error (UploadFile): {ex.Message}");
            }
            return null;
        }

        public async Task<bool> CreateIssueAsync(RedmineIssue issue)
        {
            try
            {
                var requestObject = new RedmineIssueRequest { Issue = issue };
                var json = JsonConvert.SerializeObject(requestObject, new JsonSerializerSettings 
                { 
                    NullValueHandling = NullValueHandling.Ignore 
                });
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_baseUrl}issues.json", content);
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redmine Error (CreateIssue): {ex.Message}");
                return false;
            }
        }
    }
}
