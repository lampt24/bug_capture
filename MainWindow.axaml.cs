using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BugCapture.Models;
using BugCapture.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using ShareX.ImageEditor.Presentation.Views;
using ShareX.ImageEditor.Presentation.ViewModels;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Net.Http;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ShareX.HelpersLib;
using Redmine.Net.Api.Types;
using Redmine.Net.Api;

namespace BugCapture
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly ShareXCaptureService _captureService;
        private const int ThumbnailWidthPixels = 100;
        private const int ThumbnailQualityPercent = 80;
        private const string GenericFileIconPathData = "M14,2H6C4.9,2 4,2.9 4,4V20C4,21.1 4.9,22 6,22H18C19.1,22 20,21.1 20,20V8L14,2M14,9V3.5L18.5,9H14Z";
        private const string PdfFileIconPathData = "M6,2H14L20,8V20C20,21.1 19.1,22 18,22H6C4.9,22 4,21.1 4,20V4C4,2.9 4.9,2 6,2M8,12V18H10V16H12C13.1,16 14,15.1 14,14C14,12.9 13.1,12 12,12H8M10,14H12V14H10V14M15,12V18H17V12H15Z";
        private const string WordFileIconPathData = "M6,2H14L20,8V20C20,21.1 19.1,22 18,22H6C4.9,22 4,21.1 4,20V4C4,2.9 4.9,2 6,2M8,12L9.2,18H10.8L12,14.5L13.2,18H14.8L16,12H14.5L13.9,16L12.7,12H11.3L10.1,16L9.5,12H8Z";
        private const string ArchiveFileIconPathData = "M20.54,5.23L19.15,3.55C18.88,3.21 18.47,3 18,3H6C5.53,3 5.12,3.21 4.85,3.55L3.46,5.23C3.17,5.57 3,6 3,6.5V19C3,20.1 3.9,21 5,21H19C20.1,21 21,20.1 21,19V6.5C21,6 20.83,5.57 20.54,5.23M12,17L8,13H10.5V11H13.5V13H16L12,17M5.12,5L6,4H18L18.88,5H5.12Z";
        private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"
        };
        private bool _isInitialized = false;
        private int _evidenceCounter = 1;
        private string _statusText = "Ready";
        private string _bugTitle = string.Empty;
        private string _bugPrefix = string.Empty;
        private string _bugDescription = string.Empty;
        private string _assignee = string.Empty;
        private string _category = string.Empty;
        private string _whoMiss = string.Empty;
        private string _moduleId = string.Empty;
        private CapturedImage? _currentlyEditingImage;
        private bool _isEditorVisible = false;
        private string _currentDateTime = string.Empty;
        private System.Threading.Timer? _clockTimer;
        private readonly Logger _logger = new Logger();

        // Redmine Integration
        private readonly RedmineService _redmineService;
        private readonly MattermostService _mattermostService;
        private const int ProjectIndentSpacesPerLevel = 2;
        private const int RootParentProjectId = 0;
        private const string FixedTrackerName = "Bug";
        private const string MattermostIntegrationUrl = "http://localhost";
        private int _defaultStatusId = 1;
        private string _redmineUrl = string.Empty;
        private string _redmineApiKey = string.Empty;
        private ObservableCollection<ProjectComboItem> _projects = new();
        private ObservableCollection<ProjectTracker> _trackers = new();
        private Project? _selectedProject;
        private ProjectComboItem? _selectedProjectItem;
        private ProjectTracker? _selectedTracker;
        private ObservableCollection<ProjectMembership> _memberships = new();
        private ProjectMembership? _selectedMembership;
        private ObservableCollection<CustomFieldControlViewModel> _customFieldControls = new();
        private bool _isSubmitting = false;
        private bool _isTrackerSelectionEnabled = true;
        private string _mattermostServerUrl = string.Empty;
        private string _mattermostAccessToken = string.Empty;
        private bool _isMattermostReady;
        private string _mattermostConnectionStatus = string.Empty;
        private ObservableCollection<MattermostChannelInfo> _mattermostChannels = new();
        private ObservableCollection<MattermostChannelTreeItem> _mattermostChannelTreeItems = new();
        private MattermostChannelTreeItem? _selectedMattermostChannelTreeItem;
        private ObservableCollection<MattermostUserInfo> _mattermostUsers = new();
        private bool _sendToMattermostChannels;
        private bool _mentionMattermostUsers;
        private bool _replyToMattermostThreadId;
        private string _mattermostChannelTargetsText = string.Empty;
        private string _mattermostMentionText = string.Empty;
        private string _mattermostThreadId = string.Empty;
        private ObservableCollection<string> _mattermostMentionSuggestions = new();
        private bool _isRefreshingMentionSuggestions;
        private bool _isApplyingMentionSuggestion;
        private bool _isApplyingMattermostSettings;
        private string _lastCreatedIssueUrl = string.Empty;
        private string _lastCreatedIssueText = string.Empty;
        private Point? _thumbnailDragStartPoint;
        private CapturedImage? _thumbnailDragSource;
        private bool _isThumbnailDragInProgress;
        private const string ThumbnailDragDataPrefix = "BUGCAPTURE_THUMBNAIL:";

        public ObservableCollection<CapturedImage> CapturedImages { get; } = new ObservableCollection<CapturedImage>();
        public MainViewModel EditorViewModel { get; } = new MainViewModel();

        public string BugTitle
        {
            get => _bugTitle;
            set { _bugTitle = value; OnPropertyChanged(nameof(BugTitle)); }
        }

        public string BugPrefix
        {
            get => _bugPrefix;
            set { _bugPrefix = value; OnPropertyChanged(nameof(BugPrefix)); SaveSelections(); }
        }

        public string BugDescription
        {
            get => _bugDescription;
            set { _bugDescription = value; OnPropertyChanged(nameof(BugDescription)); }
        }

        public string Assignee
        {
            get => _assignee;
            set { _assignee = value; OnPropertyChanged(nameof(Assignee)); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(nameof(Category)); }
        }

        public string WhoMiss
        {
            get => _whoMiss;
            set { _whoMiss = value; OnPropertyChanged(nameof(WhoMiss)); }
        }

        public string ModuleId
        {
            get => _moduleId;
            set { _moduleId = value; OnPropertyChanged(nameof(ModuleId)); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsEditorVisible
        {
            get => _isEditorVisible;
            set
            {
                if (_isEditorVisible == value)
                {
                    return;
                }

                _isEditorVisible = value;
                OnPropertyChanged(nameof(IsEditorVisible));
            }
        }

        public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public string CurrentDateTime
        {
            get => _currentDateTime;
            private set { _currentDateTime = value; OnPropertyChanged(nameof(CurrentDateTime)); }
        }

        // Redmine Properties
        public string RedmineUrl
        {
            get => _redmineUrl;
            set { _redmineUrl = value; OnPropertyChanged(nameof(RedmineUrl)); }
        }

        public string RedmineApiKey
        {
            get => _redmineApiKey;
            set { _redmineApiKey = value; OnPropertyChanged(nameof(RedmineApiKey)); }
        }

        public ObservableCollection<ProjectComboItem> Projects
        {
            get => _projects;
            set { _projects = value; OnPropertyChanged(nameof(Projects)); }
        }

        public ProjectComboItem? SelectedProjectItem
        {
            get => _selectedProjectItem;
            set
            {
                if (_selectedProjectItem != value)
                {
                    _selectedProjectItem = value;
                    OnPropertyChanged(nameof(SelectedProjectItem));
                    SelectedProject = value?.Project;
                }
            }
        }

        public ObservableCollection<ProjectTracker> Trackers
        {
            get => _trackers;
            set { _trackers = value; OnPropertyChanged(nameof(Trackers)); }
        }

        public Project? SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (_selectedProject != value)
                {
                    _selectedProject = value;
                    OnPropertyChanged(nameof(SelectedProject));
                    OnProjectSelected();
                    SaveSelections();
                }
            }
        }

        public ProjectTracker? SelectedTracker
        {
            get => _selectedTracker;
            set
            {
                if (_selectedTracker != value)
                {
                    // Save custom fields for the outgoing tracker
                    if (_isInitialized) SaveCustomFieldValues();

                    _selectedTracker = value;
                    OnPropertyChanged(nameof(SelectedTracker));
                    OnTrackerSelected();
                    SaveSelections();
                }
            }
        }

        public bool IsTrackerSelectionEnabled
        {
            get => _isTrackerSelectionEnabled;
            set
            {
                if (_isTrackerSelectionEnabled != value)
                {
                    _isTrackerSelectionEnabled = value;
                    OnPropertyChanged(nameof(IsTrackerSelectionEnabled));
                }
            }
        }

        public ObservableCollection<ProjectMembership> Memberships
        {
            get => _memberships;
            set { _memberships = value; OnPropertyChanged(nameof(Memberships)); }
        }

        public ProjectMembership? SelectedMembership
        {
            get => _selectedMembership;
            set
            {
                if (_selectedMembership != value)
                {
                    _selectedMembership = value;
                    OnPropertyChanged(nameof(SelectedMembership));
                    SaveSelections();
                }
            }
        }

        private void SaveSelections(bool force = false)
        {
            // Don't save if we're still initializing
            if (!_isInitialized && !force) return;

            var settings = SettingsService.Load();
            settings.LastProjectId = SelectedProject?.Id;
            settings.LastTrackerId = SelectedTracker?.Id;
            settings.LastAssigneeId = SelectedMembership?.User?.Id;
            settings.LastIssuePrefix = BugPrefix;
            SettingsService.Save(settings);
        }

        private void SaveCustomFieldValues()
        {
            if (!_isInitialized || SelectedTracker == null) return;

            var settings = SettingsService.Load();
            var trackerIdStr = SelectedTracker.Id.ToString();

            if (!settings.TrackerCustomFields.ContainsKey(trackerIdStr))
                settings.TrackerCustomFields[trackerIdStr] = new();

            foreach (var ctrl in CustomFieldControls)
            {
                if (ctrl.Field != null)
                {
                    var fieldIdStr = ctrl.Field.Id.ToString();
                    settings.TrackerCustomFields[trackerIdStr][fieldIdStr] = ctrl.Value?.ToString() ?? string.Empty;
                }
            }
            SettingsService.Save(settings);
        }

        public ObservableCollection<CustomFieldControlViewModel> CustomFieldControls
        {
            get => _customFieldControls;
            set { _customFieldControls = value; OnPropertyChanged(nameof(CustomFieldControls)); }
        }

        public bool IsSubmitting
        {
            get => _isSubmitting;
            set { _isSubmitting = value; OnPropertyChanged(nameof(IsSubmitting)); }
        }

        public string MattermostServerUrl
        {
            get => _mattermostServerUrl;
            set { _mattermostServerUrl = value; OnPropertyChanged(nameof(MattermostServerUrl)); }
        }

        public string MattermostAccessToken
        {
            get => _mattermostAccessToken;
            set { _mattermostAccessToken = value; OnPropertyChanged(nameof(MattermostAccessToken)); }
        }

        public ObservableCollection<MattermostChannelInfo> MattermostChannels
        {
            get => _mattermostChannels;
            set { _mattermostChannels = value; OnPropertyChanged(nameof(MattermostChannels)); }
        }

        public ObservableCollection<MattermostChannelTreeItem> MattermostChannelTreeItems
        {
            get => _mattermostChannelTreeItems;
            set { _mattermostChannelTreeItems = value; OnPropertyChanged(nameof(MattermostChannelTreeItems)); }
        }

        public MattermostChannelTreeItem? SelectedMattermostChannelTreeItem
        {
            get => _selectedMattermostChannelTreeItem;
            set { _selectedMattermostChannelTreeItem = value; OnPropertyChanged(nameof(SelectedMattermostChannelTreeItem)); }
        }

        public ObservableCollection<MattermostUserInfo> MattermostUsers
        {
            get => _mattermostUsers;
            set { _mattermostUsers = value; OnPropertyChanged(nameof(MattermostUsers)); }
        }

        public bool IsMattermostReady
        {
            get => _isMattermostReady;
            private set
            {
                _isMattermostReady = value;
                OnPropertyChanged(nameof(IsMattermostReady));
            }
        }

        public string MattermostConnectionStatus
        {
            get => _mattermostConnectionStatus;
            private set
            {
                _mattermostConnectionStatus = value;
                OnPropertyChanged(nameof(MattermostConnectionStatus));
            }
        }

        public bool SendToMattermostChannels
        {
            get => _sendToMattermostChannels;
            set
            {
                if (_sendToMattermostChannels == value)
                {
                    return;
                }

                _sendToMattermostChannels = value;
                OnPropertyChanged(nameof(SendToMattermostChannels));

                if (value && _mentionMattermostUsers)
                {
                    _mentionMattermostUsers = false;
                    OnPropertyChanged(nameof(MentionMattermostUsers));
                    MattermostMentionSuggestions.Clear();
                    OnPropertyChanged(nameof(IsMattermostMentionSuggestionVisible));
                }

                if (value && _replyToMattermostThreadId)
                {
                    _replyToMattermostThreadId = false;
                    OnPropertyChanged(nameof(ReplyToMattermostThreadId));
                }

                SaveMattermostSelections();
            }
        }

        public bool MentionMattermostUsers
        {
            get => _mentionMattermostUsers;
            set
            {
                if (_mentionMattermostUsers == value)
                {
                    return;
                }

                _mentionMattermostUsers = value;
                OnPropertyChanged(nameof(MentionMattermostUsers));

                if (_mentionMattermostUsers)
                {
                    if (_sendToMattermostChannels)
                    {
                        _sendToMattermostChannels = false;
                        OnPropertyChanged(nameof(SendToMattermostChannels));
                    }

                    if (_replyToMattermostThreadId)
                    {
                        _replyToMattermostThreadId = false;
                        OnPropertyChanged(nameof(ReplyToMattermostThreadId));
                    }

                    RefreshMattermostMentionSuggestions();
                }
                else
                {
                    MattermostMentionSuggestions.Clear();
                    OnPropertyChanged(nameof(IsMattermostMentionSuggestionVisible));
                }

                SaveMattermostSelections();
            }
        }

        public bool ReplyToMattermostThreadId
        {
            get => _replyToMattermostThreadId;
            set
            {
                if (_replyToMattermostThreadId == value)
                {
                    return;
                }

                _replyToMattermostThreadId = value;
                OnPropertyChanged(nameof(ReplyToMattermostThreadId));

                if (_replyToMattermostThreadId)
                {
                    if (_sendToMattermostChannels)
                    {
                        _sendToMattermostChannels = false;
                        OnPropertyChanged(nameof(SendToMattermostChannels));
                    }

                    if (_mentionMattermostUsers)
                    {
                        _mentionMattermostUsers = false;
                        OnPropertyChanged(nameof(MentionMattermostUsers));
                        MattermostMentionSuggestions.Clear();
                        OnPropertyChanged(nameof(IsMattermostMentionSuggestionVisible));
                    }
                }

                SaveMattermostSelections();
            }
        }

        public string MattermostChannelTargetsText
        {
            get => _mattermostChannelTargetsText;
            set
            {
                if (string.Equals(_mattermostChannelTargetsText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _mattermostChannelTargetsText = value;
                OnPropertyChanged(nameof(MattermostChannelTargetsText));
                SaveMattermostSelections();
            }
        }

        public string MattermostMentionText
        {
            get => _mattermostMentionText;
            set
            {
                if (string.Equals(_mattermostMentionText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _mattermostMentionText = value;
                OnPropertyChanged(nameof(MattermostMentionText));

                if (MentionMattermostUsers && !_isApplyingMentionSuggestion)
                {
                    RefreshMattermostMentionSuggestions();
                }

                SaveMattermostSelections();
            }
        }

        public string MattermostThreadId
        {
            get => _mattermostThreadId;
            set
            {
                if (string.Equals(_mattermostThreadId, value, StringComparison.Ordinal))
                {
                    return;
                }

                _mattermostThreadId = value;
                OnPropertyChanged(nameof(MattermostThreadId));
                SaveMattermostSelections();
            }
        }

        public ObservableCollection<string> MattermostMentionSuggestions
        {
            get => _mattermostMentionSuggestions;
            set
            {
                _mattermostMentionSuggestions = value;
                OnPropertyChanged(nameof(MattermostMentionSuggestions));
                OnPropertyChanged(nameof(IsMattermostMentionSuggestionVisible));
            }
        }

        public bool IsMattermostMentionSuggestionVisible => MentionMattermostUsers && MattermostMentionSuggestions.Count > 0;

        public string LastCreatedIssueUrl
        {
            get => _lastCreatedIssueUrl;
            private set
            {
                _lastCreatedIssueUrl = value;
                OnPropertyChanged(nameof(LastCreatedIssueUrl));
                OnPropertyChanged(nameof(IsLastCreatedIssueVisible));
            }
        }

        public string LastCreatedIssueText
        {
            get => _lastCreatedIssueText;
            private set
            {
                _lastCreatedIssueText = value;
                OnPropertyChanged(nameof(LastCreatedIssueText));
            }
        }

        public bool IsLastCreatedIssueVisible => !string.IsNullOrWhiteSpace(LastCreatedIssueUrl);

        private void SetLastCreatedIssue(int issueId)
        {
            if (issueId <= 0 || string.IsNullOrWhiteSpace(RedmineUrl))
            {
                ClearLastCreatedIssue();
                return;
            }

            string baseUrl = RedmineUrl.TrimEnd('/');
            LastCreatedIssueUrl = $"{baseUrl}/issues/{issueId}";
            LastCreatedIssueText = $"Issue #{issueId}";
        }

        private void ClearLastCreatedIssue()
        {
            LastCreatedIssueUrl = string.Empty;
            LastCreatedIssueText = string.Empty;
        }

        public void OnOpenLastIssueClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LastCreatedIssueUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = LastCreatedIssueUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText = $"Cannot open issue link: {ex.Message}";
            }
        }

        private void UpdateClock(object? state)
        {
            var now = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CurrentDateTime = now);
        }
        public string ThemeIcon => (Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light) ? "M12,2A9,9 0 0,0 3,11A9,9 0 0,0 12,20A9,9 0 0,0 21,11A9,9 0 0,0 20.93,9.75C20.35,10.55 19.41,11 18.33,11A4.33,4.33 0 0,1 14,6.67C14,5.27 15.08,4.11 16.5,4C15.17,2.73 13.5,2 12,2Z" : "M12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7M12,3.5L14.12,5.62L12,7.74L9.88,5.62L12,3.5M12,20.5L14.12,18.38L12,16.26L9.88,18.38L12,20.5M20.5,12L18.38,14.12L16.26,12L18.38,9.88L20.5,12M3.5,12L5.62,14.12L7.74,12L5.62,9.88L3.5,12M18.38,5.62L18.38,8.62L15.38,5.62H18.38M5.62,18.38V15.38L8.62,18.38H5.62M5.62,5.62H8.62L5.62,8.62V5.62M18.38,18.38H15.38L18.38,15.38V18.38Z";

        public MainWindow()
        {
            InitializeComponent();
            _captureService = new ShareXCaptureService();
            // Setup logging to file in LocalAppData (safer)
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string logDir = System.IO.Path.Combine(appData, "BugCapture", "logs");
                if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
                string logPath = System.IO.Path.Combine(logDir, "bugcapture_ui.log");

                _logger = new Logger(logPath);
                _logger.AsyncWrite = false;
                _logger.WriteLine($"--- UI Initialized at {DateTime.Now} ---");
                StatusText = $"Log: {logPath}";
            }
            catch (Exception ex)
            {
                StatusText = $"Logger Init Error: {ex.Message}";
            }

            EditorViewModel.ShowTaskModeButtons = false;

            // Subscribe to editor events
            EditorViewModel.SaveRequested += OnEditorSaveRequested;
            EditorViewModel.CopyRequested += OnEditorCopyRequested;

            // Start live clock
            UpdateClock(null);
            _clockTimer = new System.Threading.Timer(UpdateClock, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            DataContext = this;

            _redmineService = new RedmineService();
            _mattermostService = new MattermostService();
            // Load initial settings
            var settings = SettingsService.Load();
            RedmineUrl = settings.RedmineUrl;
            RedmineApiKey = settings.RedmineApiKey;
            MattermostServerUrl = settings.MattermostServerUrl;
            MattermostAccessToken = settings.MattermostAccessToken;

            // Apply Theme
            if (Avalonia.Application.Current != null)
            {
                Avalonia.Application.Current.RequestedThemeVariant =
                    settings.IsDarkMode ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
            }

            BugPrefix = settings.LastIssuePrefix;
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (_isInitialized) return;

            var settings = SettingsService.Load();
            if (!settings.IsValid())
            {
                await ShowSettingsDialog();
            }
            else
            {
                await LoadRedmineData();
                await LoadMattermostData();
            }
            _isInitialized = true;
        }

        private async Task ShowSettingsDialog()
        {
            var sw = new SettingsWindow();
            await sw.ShowDialog(this);
            if (sw.IsSaved)
            {
                RedmineUrl = sw.CurrentSettings.RedmineUrl;
                RedmineApiKey = sw.CurrentSettings.RedmineApiKey;
                MattermostServerUrl = sw.CurrentSettings.MattermostServerUrl;
                MattermostAccessToken = sw.CurrentSettings.MattermostAccessToken;
                _ = LoadRedmineData();
                _ = LoadMattermostData();
            }
            else
            {
                // If closing/cancelling and setup is still invalid, exit app
                var settings = SettingsService.Load();
                if (!settings.IsValid())
                {
                    Close();
                }
            }
        }

        private async void OnOpenSettingsClick(object sender, RoutedEventArgs e)
        {
            await ShowSettingsDialog();
        }

        private async Task LoadRedmineData()
        {
            StatusText = "Connecting to Redmine...";
            _redmineService.Initialize(RedmineUrl, RedmineApiKey);

            var projects = await _redmineService.GetProjectsAsync();
            Projects = BuildProjectTreeItems(projects);

            // Fetch default status ID
            var statuses = await _redmineService.GetStatusesAsync();
            var defStatus = statuses.FirstOrDefault(s => s.IsDefault) ?? statuses.FirstOrDefault();
            if (defStatus != null)
            {
                _defaultStatusId = defStatus.Id;
                _logger.WriteLine($"Redmine: Default Status ID set to {_defaultStatusId} ({defStatus.Name})");
            }

            // Restore Selection
            var settings = SettingsService.Load();
            if (settings.LastProjectId.HasValue)
            {
                SelectedProjectItem = Projects.FirstOrDefault(p => p.Project.Id == settings.LastProjectId.Value);
            }

            if (SelectedProjectItem == null)
            {
                SelectedProjectItem = Projects.FirstOrDefault();
            }

            StatusText = Projects.Count > 0 ? "Redmine connected." : "Failed to load Redmine projects.";
        }

        private async Task LoadMattermostData()
        {
            MattermostMentionSuggestions.Clear();

            if (string.IsNullOrWhiteSpace(MattermostServerUrl) || string.IsNullOrWhiteSpace(MattermostAccessToken))
            {
                IsMattermostReady = false;
                MattermostConnectionStatus = "Mattermost chưa được cấu hình.";
                MattermostChannels = new ObservableCollection<MattermostChannelInfo>();
                MattermostChannelTreeItems = new ObservableCollection<MattermostChannelTreeItem>();
                MattermostUsers = new ObservableCollection<MattermostUserInfo>();
                SendToMattermostChannels = false;
                MentionMattermostUsers = false;
                ReplyToMattermostThreadId = false;
                return;
            }

            try
            {
                _logger.WriteLine($"[Mattermost] Load start | Server={MattermostServerUrl}");
                _mattermostService.Initialize(MattermostServerUrl, MattermostAccessToken);
                var (channels, users) = await _mattermostService.GetChannelsAndMembersAsync();
                _logger.WriteLine($"[Mattermost] API loaded | Channels={channels.Count} | Users={users.Count}");

                if (users.Count > 0)
                {
                    var sampleUsers = string.Join(", ", users
                        .Take(10)
                        .Select(u => string.IsNullOrWhiteSpace(u.Username) ? u.Id : u.Username));
                    _logger.WriteLine($"[Mattermost] User sample (max 10): {sampleUsers}");
                }
                else
                {
                    _logger.WriteLine("[Mattermost] WARNING: User list is empty. Mention suggestion will not work.");
                }

                MattermostChannels = new ObservableCollection<MattermostChannelInfo>(channels);
                MattermostChannelTreeItems = BuildMattermostChannelTreeItems(channels);
                MattermostUsers = new ObservableCollection<MattermostUserInfo>(users);
                ApplyMattermostSelections();
                IsMattermostReady = true;
                MattermostConnectionStatus = "Mattermost connected.";
                StatusText = $"Mattermost ready: {channels.Count} channels, {users.Count} users.";
            }
            catch (Exception ex)
            {
                IsMattermostReady = false;
                MattermostConnectionStatus = "Mattermost không kết nối được hoặc access token không hợp lệ.";
                MattermostChannels = new ObservableCollection<MattermostChannelInfo>();
                MattermostChannelTreeItems = new ObservableCollection<MattermostChannelTreeItem>();
                MattermostUsers = new ObservableCollection<MattermostUserInfo>();
                SendToMattermostChannels = false;
                MentionMattermostUsers = false;
                ReplyToMattermostThreadId = false;
                StatusText = $"Mattermost load failed: {ex.Message}";
                _logger.WriteException(ex, "Mattermost Load Error");
            }
        }

        private static ObservableCollection<MattermostChannelTreeItem> BuildMattermostChannelTreeItems(List<MattermostChannelInfo> channels)
        {
            var items = new ObservableCollection<MattermostChannelTreeItem>();
            var grouped = channels
                .GroupBy(c => string.IsNullOrWhiteSpace(c.TeamDisplayName) ? c.TeamName : c.TeamDisplayName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                items.Add(new MattermostChannelTreeItem
                {
                    IsTeamHeader = true,
                    DisplayName = group.Key
                });

                foreach (var channel in group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(new MattermostChannelTreeItem
                    {
                        IsTeamHeader = false,
                        DisplayName = "  " + channel.DisplayText,
                        Channel = channel
                    });
                }
            }

            return items;
        }

        private void EnsureDefaultMattermostMention()
        {
            if (SelectedMembership?.User == null || MattermostUsers.Count == 0)
            {
                return;
            }

            var redmineName = (SelectedMembership.User.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(redmineName))
            {
                return;
            }

            var user = FindMattermostUserByName(redmineName);

            if (user == null)
            {
                var fallbackMention = BuildMentionHandle(redmineName);
                if (!string.IsNullOrWhiteSpace(fallbackMention))
                {
                    var existingFallback = ParseMentionHandles(MattermostMentionText);
                    if (!existingFallback.Contains(fallbackMention, StringComparer.OrdinalIgnoreCase))
                    {
                        MattermostMentionText = string.IsNullOrWhiteSpace(MattermostMentionText)
                            ? fallbackMention
                            : MattermostMentionText + " " + fallbackMention;
                    }
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(MattermostMentionText))
            {
                MattermostMentionText = user.MentionHandle;
                return;
            }

            var existingMentions = ParseMentionHandles(MattermostMentionText);
            if (!existingMentions.Contains(user.MentionHandle, StringComparer.OrdinalIgnoreCase))
            {
                MattermostMentionText += " " + user.MentionHandle;
            }
        }

        public void OnMattermostMentionTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (!MentionMattermostUsers)
            {
                MattermostMentionSuggestions.Clear();
                OnPropertyChanged(nameof(IsMattermostMentionSuggestionVisible));
                return;
            }

            if (sender is TextBox textBox)
            {
                MattermostMentionText = textBox.Text ?? string.Empty;
            }
        }

        private void RefreshMattermostMentionSuggestions()
        {
            if (_isRefreshingMentionSuggestions || _isApplyingMentionSuggestion)
            {
                return;
            }

            _isRefreshingMentionSuggestions = true;

            try
            {
                if (!MentionMattermostUsers)
                {
                    MattermostMentionSuggestions = new ObservableCollection<string>();
                    return;
                }

                if (MattermostUsers.Count == 0)
                {
                    MattermostMentionSuggestions = new ObservableCollection<string>();
                    return;
                }

                var currentText = MattermostMentionText ?? string.Empty;
                if (!TryGetCurrentMentionQuery(currentText, out var query))
                {
                    MattermostMentionSuggestions = new ObservableCollection<string>();
                    return;
                }

                var suggestions = MattermostUsers
                    .Where(u => string.IsNullOrWhiteSpace(query) || u.Matches(query))
                    .OrderBy(u => u.Username)
                    .Take(8)
                    .Select(u => u.DisplayText)
                    .ToList();

                MattermostMentionSuggestions = new ObservableCollection<string>(suggestions);
            }
            finally
            {
                _isRefreshingMentionSuggestions = false;
            }
        }

        public void OnMattermostChannelTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selected = SelectedMattermostChannelTreeItem;
            if (selected == null || selected.IsTeamHeader || selected.Channel == null)
            {
                return;
            }

            // Single-select behavior: combobox choice is the only source of channel target.
            MattermostChannelTargetsText = selected.Channel.Reference;
            SaveMattermostSelections();
        }

        private void SaveMattermostSelections()
        {
            if (!_isInitialized || _isApplyingMattermostSettings)
            {
                return;
            }

            var settings = SettingsService.Load();
            settings.MattermostSendToChannels = SendToMattermostChannels;
            settings.MattermostMentionUsers = MentionMattermostUsers;
            settings.MattermostReplyToThreadId = ReplyToMattermostThreadId;
            settings.MattermostSelectedChannelReference = MattermostChannelTargetsText ?? string.Empty;
            settings.MattermostMentionText = MattermostMentionText ?? string.Empty;
            settings.MattermostThreadId = MattermostThreadId ?? string.Empty;
            SettingsService.Save(settings);
        }

        private void ApplyMattermostSelections()
        {
            var settings = SettingsService.Load();

            _isApplyingMattermostSettings = true;
            try
            {
                MattermostMentionText = settings.MattermostMentionText ?? string.Empty;
                MattermostThreadId = settings.MattermostThreadId ?? string.Empty;

                var savedReference = settings.MattermostSelectedChannelReference?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(savedReference))
                {
                    var targetName = savedReference.TrimStart('#');
                    var channel = MattermostChannels.FirstOrDefault(c =>
                        string.Equals(c.Reference, savedReference, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase));

                    if (channel != null)
                    {
                        var selectedItem = MattermostChannelTreeItems.FirstOrDefault(i =>
                            !i.IsTeamHeader && i.Channel != null && string.Equals(i.Channel.Id, channel.Id, StringComparison.OrdinalIgnoreCase));

                        if (selectedItem != null)
                        {
                            SelectedMattermostChannelTreeItem = selectedItem;
                            MattermostChannelTargetsText = channel.Reference;
                        }
                    }
                }

                if (settings.MattermostReplyToThreadId)
                {
                    ReplyToMattermostThreadId = true;
                }
                else if (settings.MattermostMentionUsers)
                {
                    MentionMattermostUsers = true;
                }
                else
                {
                    SendToMattermostChannels = true;
                }
            }
            finally
            {
                _isApplyingMattermostSettings = false;
            }
        }

        public void OnMattermostMentionSuggestionSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox)
            {
                return;
            }

            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not string selected)
            {
                return;
            }

            var mention = selected.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(mention) || !mention.StartsWith("@", StringComparison.Ordinal))
            {
                listBox.SelectedIndex = -1;
                return;
            }

            _isApplyingMentionSuggestion = true;
            MattermostMentionText = ReplaceCurrentMentionToken(MattermostMentionText, mention);
            listBox.SelectedIndex = -1;
            MattermostMentionSuggestions = new ObservableCollection<string>();
            _isApplyingMentionSuggestion = false;
        }

        private static bool TryGetCurrentMentionQuery(string text, out string query)
        {
            query = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmedEnd = text.TrimEnd();
            if (trimmedEnd.Length == 0)
            {
                return false;
            }

            var lastSpace = trimmedEnd.LastIndexOfAny(new[] { ' ', '\t', '\n', '\r' });
            var token = lastSpace >= 0 ? trimmedEnd[(lastSpace + 1)..] : trimmedEnd;
            if (!token.StartsWith("@", StringComparison.Ordinal))
            {
                return false;
            }

            query = token.Length > 1 ? token[1..] : string.Empty;
            return true;
        }

        private static string ReplaceCurrentMentionToken(string text, string mention)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return mention;
            }

            var updated = Regex.Replace(text, @"(?:^|\s)@[a-zA-Z0-9._-]*$", m => m.Value.StartsWith(" ", StringComparison.Ordinal) ? " " + mention : mention);
            if (!updated.EndsWith(" ", StringComparison.Ordinal))
            {
                updated += " ";
            }

            return updated;
        }

        private List<MattermostChannelInfo> ResolveMattermostChannels(string input)
        {
            var requested = input
                .Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('#'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return MattermostChannels
                .Where(c => requested.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                         || requested.Contains(c.DisplayName, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        private List<string> ParseMentionHandles(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            var matches = Regex.Matches(input, @"@([a-zA-Z0-9._-]+)");
            return matches
                .Select(m => "@" + m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<MattermostUserInfo> ResolveMattermostUsers(string input)
        {
            var handles = ParseMentionHandles(input)
                .Select(h => h.TrimStart('@'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return MattermostUsers
                .Where(u => handles.Contains(u.Username))
                .ToList();
        }

        private static string ExtractMattermostPostIdFromInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var trimmed = input.Trim();
            var match = Regex.Match(trimmed, @"([a-z0-9]{26})(?!.*[a-z0-9]{26})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return trimmed;
        }

        private string BuildMentionHandle(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var value = source.Trim();
            if (value.StartsWith("@", StringComparison.Ordinal))
            {
                return value;
            }

            var matchedUser = FindMattermostUserByName(value);

            if (matchedUser != null)
            {
                return matchedUser.MentionHandle;
            }

            var compact = Regex.Replace(value, @"\s+", string.Empty);
            var normalized = Regex.Replace(compact, @"[^a-zA-Z0-9._-]", string.Empty);
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : "@" + normalized;
        }

        private MattermostUserInfo? FindMattermostUserByName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || MattermostUsers.Count == 0)
            {
                return null;
            }

            var normalized = value.Trim();
            var lowered = normalized.ToLowerInvariant();
            var normalizedKey = Regex.Replace(lowered, @"[^a-z0-9]", string.Empty);

            static string BuildUserFullName(MattermostUserInfo u) => (u.FirstName + " " + u.LastName).Trim();
            static string NormalizeKey(string s) => Regex.Replace((s ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]", string.Empty);

            // Prioritize exact username to keep mention deterministic.
            var exactUsername = MattermostUsers.FirstOrDefault(u =>
                string.Equals(u.Username, normalized, StringComparison.OrdinalIgnoreCase));
            if (exactUsername != null)
            {
                return exactUsername;
            }

            return MattermostUsers.FirstOrDefault(u =>
                       string.Equals(BuildUserFullName(u), normalized, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(u.Nickname, normalized, StringComparison.OrdinalIgnoreCase)
                       || BuildUserFullName(u).ToLowerInvariant().Contains(lowered)
                       || (!string.IsNullOrWhiteSpace(u.FirstName) && string.Equals(u.FirstName, normalized, StringComparison.OrdinalIgnoreCase))
                       || (!string.IsNullOrWhiteSpace(u.LastName) && string.Equals(u.LastName, normalized, StringComparison.OrdinalIgnoreCase))
                       || (!string.IsNullOrWhiteSpace(normalizedKey) &&
                           (
                               NormalizeKey(u.Username).Contains(normalizedKey)
                               || normalizedKey.Contains(NormalizeKey(u.Username))
                               || NormalizeKey(u.Nickname).Contains(normalizedKey)
                               || normalizedKey.Contains(NormalizeKey(u.Nickname))
                               || NormalizeKey(BuildUserFullName(u)).Contains(normalizedKey)
                               || normalizedKey.Contains(NormalizeKey(BuildUserFullName(u)))
                           ))
                   );
        }

        private ObservableCollection<ProjectComboItem> BuildProjectTreeItems(List<Project> sourceProjects)
        {
            var result = new ObservableCollection<ProjectComboItem>();
            if (sourceProjects == null || sourceProjects.Count == 0)
            {
                return result;
            }

            var groupedByParent = sourceProjects
                .GroupBy(project => project.Parent?.Id ?? RootParentProjectId)
                .ToDictionary(group => group.Key, group => group.OrderBy(project => project.Name).ToList());

            var visitedProjectIds = new HashSet<int>();

            void AppendChildren(int parentId, int level)
            {
                if (!groupedByParent.TryGetValue(parentId, out var children))
                {
                    return;
                }

                foreach (var child in children)
                {
                    if (!visitedProjectIds.Add(child.Id))
                    {
                        continue;
                    }

                    string indent = new string(' ', level * ProjectIndentSpacesPerLevel);
                    result.Add(new ProjectComboItem
                    {
                        Project = child,
                        DisplayName = $"{indent}{child.Name}"
                    });

                    AppendChildren(child.Id, level + 1);
                }
            }

            AppendChildren(RootParentProjectId, 0);

            foreach (var project in sourceProjects.OrderBy(project => project.Name))
            {
                if (visitedProjectIds.Add(project.Id))
                {
                    result.Add(new ProjectComboItem
                    {
                        Project = project,
                        DisplayName = project.Name
                    });
                }
            }

            return result;
        }

        private async void OnProjectSelected()
        {
            CustomFieldControls.Clear(); // Clear fields first
            Trackers.Clear();
            SelectedTracker = null;
            IsTrackerSelectionEnabled = true;
            Memberships.Clear();

            if (SelectedProject != null)
            {
                var trackers = await _redmineService.GetTrackersForProjectAsync(SelectedProject.Identifier);
                Trackers = new ObservableCollection<ProjectTracker>(trackers);

                var fixedTracker = Trackers.FirstOrDefault(t => string.Equals(t.Name?.Trim(), FixedTrackerName, StringComparison.OrdinalIgnoreCase));
                bool hasApiCustomFields = await _redmineService.HasApiCustomFieldsAsync();

                var members = await _redmineService.GetMembershipsAsync(SelectedProject.Identifier, SelectedProject.Id);
                Memberships = new ObservableCollection<ProjectMembership>(members.Where(m => m.User != null));

                // Restore Selections
                var settings = SettingsService.Load();
                if (fixedTracker != null && !hasApiCustomFields)
                {
                    SelectedTracker = fixedTracker;
                    IsTrackerSelectionEnabled = false;
                }
                else
                {
                    if (settings.LastTrackerId.HasValue)
                    {
                        SelectedTracker = Trackers.FirstOrDefault(t => t.Id == settings.LastTrackerId.Value);
                    }
                    if (SelectedTracker == null) SelectedTracker = Trackers.FirstOrDefault();
                }

                if (settings.LastAssigneeId.HasValue)
                {
                    SelectedMembership = Memberships.FirstOrDefault(m => m.User != null && m.User.Id == settings.LastAssigneeId.Value);
                }
                if (SelectedMembership == null) SelectedMembership = Memberships.FirstOrDefault();
            }
        }

        private async void OnTrackerSelected()
        {
            CustomFieldControls.Clear(); // Always clear first

            if (SelectedTracker == null) return;
            if (SelectedProject == null) return;

            StatusText = $"Loading custom fields for {SelectedTracker.Name}...";
            var allFields = await _redmineService.GetCustomFieldsForTrackerAsync(SelectedProject.Identifier, SelectedTracker.Id);
            var settings = SettingsService.Load();
            var trackerIdStr = SelectedTracker.Id.ToString();

            foreach (var field in allFields)
            {
                bool trackerMatch = field.Trackers == null || field.Trackers.Count == 0 ||
                                   field.Trackers.Any(t => t.Id == SelectedTracker.Id);

                _logger?.WriteLine($"[CustomField] Id={field.Id}, Name={field.Name}, Format={field.FieldFormat}, Options={field.PossibleValues?.Count ?? 0}, TrackerMatch={trackerMatch}");

                if (trackerMatch)
                {
                    var viewModel = new CustomFieldControlViewModel(field);

                    // Restore saved value if exists
                    if (settings.TrackerCustomFields.TryGetValue(trackerIdStr, out var fieldValues))
                    {
                        if (fieldValues.TryGetValue(field.Id.ToString(), out var savedVal))
                        {
                            if (!string.IsNullOrEmpty(savedVal))
                            {
                                viewModel.Value = savedVal;
                            }
                        }
                    }

                    viewModel.PropertyChanged += (s, ev) =>
                    {
                        if (ev.PropertyName == nameof(CustomFieldControlViewModel.Value))
                        {
                            SaveCustomFieldValues();
                        }
                    };

                    CustomFieldControls.Add(viewModel);
                }
            }
            StatusText = "Custom fields ready.";
        }

        public async void OnSubmitToRedmineClick(object sender, RoutedEventArgs e)
        {
            SaveSelections(force: true);

            if (SelectedProject == null || SelectedTracker == null)
            {
                StatusText = "Please select Project and Tracker.";
                return;
            }

            if (string.IsNullOrWhiteSpace(BugTitle))
            {
                StatusText = "Please enter a Title!!!";
                return;
            }

            if (string.IsNullOrWhiteSpace(BugDescription))
            {
                StatusText = "Please enter a Description!!!";
                return;
            }

            ClearLastCreatedIssue();
            IsSubmitting = true;
            StatusText = "Uploading attachments...";

            try
            {
                var uploads = new List<Upload>();
                var attachmentsBySizeDesc = CapturedImages
                    .OrderByDescending(item =>
                    {
                        try
                        {
                            return new System.IO.FileInfo(item.FilePath).Length;
                        }
                        catch
                        {
                            return -1L;
                        }
                    })
                    .ToList();
                var attachmentFilePaths = attachmentsBySizeDesc
                    .Select(a => a.FilePath)
                    .Where(System.IO.File.Exists)
                    .ToList();

                foreach (var img in attachmentsBySizeDesc)
                {
                    Upload? upload;
                    try
                    {
                        upload = await _redmineService.UploadFileAsync(img.FilePath);
                    }
                    catch (Exception ex)
                    {
                        var friendlyMessage = BuildFriendlyUploadErrorMessage(img.FilePath, ex);
                        var uploadErrorDetail =
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - Redmine Error (UploadFile):{Environment.NewLine}{ex}";

                        _logger.WriteLine(uploadErrorDetail);
                        StatusText = friendlyMessage;
                        System.Windows.Forms.MessageBox.Show(
                            friendlyMessage,
                            "Upload thất bại",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Error);
                        return;
                    }

                    if (upload != null) uploads.Add(upload);
                }

                var mattermostDescription = BugDescription.Trim();
                var redmineDescription = BugDescription;
                var uploadedImageFileNames = new HashSet<string>(
                    uploads.Select(u => u.FileName ?? string.Empty)
                           .Where(name => !string.IsNullOrWhiteSpace(name))
                           .Where(name => CapturedImages.Any(item =>
                               item.IsImage &&
                               string.Equals(System.IO.Path.GetFileName(item.FilePath), name, StringComparison.OrdinalIgnoreCase))),
                    StringComparer.OrdinalIgnoreCase);

                if (uploadedImageFileNames.Count > 0)
                {
                    redmineDescription += "\n\n" + (System.Globalization.CultureInfo.CurrentCulture.Name.StartsWith("vi") ? "--- Bằng chứng (Evidence): ---" : "--- Evidence: ---") + "\n";
                    foreach (var fileName in uploadedImageFileNames)
                    {
                        redmineDescription += $"\n!{fileName}!";
                    }
                }

                var issue = new Issue
                {
                    Project = IdentifiableName.Create<Project>(SelectedProject.Id),
                    Tracker = IdentifiableName.Create<Tracker>(SelectedTracker.Id),
                    Status = IdentifiableName.Create<IssueStatus>(_defaultStatusId),
                    Priority = IdentifiableName.Create<IssuePriority>(2), // Normal
                    Subject = string.IsNullOrWhiteSpace(BugPrefix) ? BugTitle : $"【{BugPrefix}】{BugTitle}",
                    Description = redmineDescription,
                    Uploads = uploads,
                    CustomFields = new List<IssueCustomField>(),
                    AssignedTo = SelectedMembership?.User
                };

                foreach (var fieldCtrl in CustomFieldControls)
                {
                    if (fieldCtrl.Value == null)
                    {
                        _logger?.WriteLine($"[Submit] Field {fieldCtrl.Field.Name} (Id:{fieldCtrl.Field.Id}) is NULL/Empty. Skipping.");
                        continue;
                    }

                    string? finalValue = fieldCtrl.Value?.ToString();
                    _logger?.WriteLine($"[Submit] Field {fieldCtrl.Field.Name} (Id:{fieldCtrl.Field.Id}) -> Extracted string finalValue: '{finalValue}'");

                    if (!string.IsNullOrEmpty(finalValue))
                    {
                        var cf = IssueCustomField.CreateSingle(fieldCtrl.Field.Id, fieldCtrl.Field.Name, finalValue);
                        issue.CustomFields.Add(cf);
                        _logger?.WriteLine($"[Submit] ADDED IssueCustomField: Name='{cf.Name}', Id={cf.Id}, Multiple={cf.Multiple}, final attached value='{finalValue}'");
                    }
                    else
                    {
                        _logger?.WriteLine($"[Submit] finalValue evaluated to NullOrEmpty for {fieldCtrl.Field.Name}. Skipping addition to issue.");
                    }
                }

                StatusText = "Creating issue...";
                var createdIssue = await _redmineService.CreateIssueAsync(issue);

                if (createdIssue != null)
                {
                    StatusText = "Issue created successfully!";
                    SetLastCreatedIssue(createdIssue.Id);

                    try
                    {
                        await SendMattermostNotificationsAsync(createdIssue.Id, issue.Subject, mattermostDescription, attachmentFilePaths);
                    }
                    catch (Exception ex)
                    {
                        _logger.WriteException(ex, "Mattermost Send Error");
                        StatusText = $"Issue created, but Mattermost send failed: {ex.Message}";
                    }

                    // Optional: Clear form
                    BugTitle = string.Empty;
                    BugDescription = string.Empty;
                    CapturedImages.Clear();
                    _evidenceCounter = 1;

                    // Clear currently editing image and editor state
                    _currentlyEditingImage = null;
                    EditorViewModel.ClearCommand.Execute(null);
                    EditorViewModel.PreviewImage = null;
                    EditorViewModel.ImageFilePath = null;
                    EditorViewModel.IsDirty = false;
                    EditorViewModel.ImageDimensions = "No image";
                }
                else
                {
                    ClearLastCreatedIssue();
                    StatusText = "Failed to create Redmine issue.";
                }
            }
            catch (Exception ex)
            {
                ClearLastCreatedIssue();
                StatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsSubmitting = false;
            }
        }

        public async void OnRefreshRedmineClick(object sender, RoutedEventArgs e)
        {
            await LoadRedmineData();
        }

        private static string BuildFriendlyUploadErrorMessage(string filePath, Exception ex)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            var message = ex.Message ?? string.Empty;

            if (message.Contains("exceeds the maximum allowed file size", StringComparison.OrdinalIgnoreCase))
            {
                return $"Không thể upload '{fileName}' vì file vượt quá giới hạn dung lượng.";
            }

            if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return $"Upload '{fileName}' bị quá thời gian chờ. Vui lòng thử lại.";
            }

            return $"Không thể upload '{fileName}'. Vui lòng kiểm tra file hoặc thử lại sau.";
        }

        private async Task SendMattermostNotificationsAsync(int issueId, string issueSubject, string mattermostDescription, List<string> attachmentFilePaths)
        {
            if (!_mattermostService.IsConfigured)
            {
                _logger.WriteLine("[Mattermost] Skip send: service is not configured.");
                return;
            }

            var selectedChannel = SelectedMattermostChannelTreeItem?.Channel;
            var shouldSendChannels = SendToMattermostChannels && selectedChannel != null;
            var shouldSendUsers = MentionMattermostUsers && !string.IsNullOrWhiteSpace(MattermostMentionText);
            var threadId = ExtractMattermostPostIdFromInput(MattermostThreadId);
            var shouldReplyThread = ReplyToMattermostThreadId && !string.IsNullOrWhiteSpace(threadId);
            if (!shouldSendChannels && !shouldSendUsers && !shouldReplyThread)
            {
                _logger.WriteLine("[Mattermost] Skip send: no channel selected, no mention users, and no thread id.");
                return;
            }

            _logger.WriteLine($"[Mattermost] Begin send | Ready={IsMattermostReady} | Server={MattermostServerUrl} | IssueId={issueId} | Attachments={attachmentFilePaths.Count} | SendChannels={shouldSendChannels} | SendUsers={shouldSendUsers} | SendThread={shouldReplyThread}");
            if (selectedChannel != null)
            {
                _logger.WriteLine($"[Mattermost] Selected channel | Team={selectedChannel.TeamDisplayName} ({selectedChannel.TeamName}) | Name={selectedChannel.Name} | ChannelId={selectedChannel.Id}");
            }
            else if (SendToMattermostChannels)
            {
                _logger.WriteLine("[Mattermost] Channel send requested but SelectedMattermostChannelTreeItem is null.");
            }

            var redmineIssueUrl = string.IsNullOrWhiteSpace(RedmineUrl)
                ? string.Empty
                : RedmineUrl.TrimEnd('/') + "/issues/" + issueId;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[BUG] {issueSubject}");
            sb.AppendLine();
            sb.AppendLine(mattermostDescription);
            if (!string.IsNullOrWhiteSpace(redmineIssueUrl))
            {
                sb.AppendLine();
                sb.AppendLine("Redmine: " + redmineIssueUrl);
            }

            var message = sb.ToString().Trim();
            var assigneeMention = BuildMentionHandle(SelectedMembership?.User?.Name);
            var creatorMention = await _mattermostService.GetCurrentUserMentionAsync();
            if (string.IsNullOrWhiteSpace(creatorMention))
            {
                creatorMention = BuildMentionHandle(Environment.UserName);
            }

            var interactiveOptions = new MattermostService.MattermostInteractivePostOptions
            {
                Pretext = issueSubject,
                Text = mattermostDescription,
                Assignee = string.IsNullOrWhiteSpace(assigneeMention) ? (SelectedMembership?.User?.Name ?? string.Empty) : assigneeMention,
                Creator = creatorMention,
                CreatedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                RedmineUrl = redmineIssueUrl,
                IntegrationUrl = MattermostIntegrationUrl,
                IssueId = issueId
            };

            int channelSent = 0;
            int userSent = 0;
            int threadSent = 0;

            if (shouldSendChannels)
            {
                channelSent = await _mattermostService.SendToChannelsAsync(new[] { selectedChannel! }, message, attachmentFilePaths, interactiveOptions);
            }

            if (shouldSendUsers)
            {
                var users = ResolveMattermostUsers(MattermostMentionText);
                _logger.WriteLine($"[Mattermost] Mention input='{MattermostMentionText}' | Resolved users={users.Count}");
                if (users.Count > 0)
                {
                    userSent = await _mattermostService.SendDirectToUsersAsync(users, message, attachmentFilePaths, interactiveOptions);
                }
                else
                {
                    _logger.WriteLine("[Mattermost] No users resolved from mention input.");
                }
            }

            if (shouldReplyThread)
            {
                _logger.WriteLine($"[Mattermost] Thread input='{MattermostThreadId}' | Resolved postId='{threadId}'");
                await _mattermostService.SendToThreadAsync(threadId, message, attachmentFilePaths, interactiveOptions);
                threadSent = 1;
            }

            _logger.WriteLine($"[Mattermost] Send result | Channels={channelSent} | Users={userSent} | Threads={threadSent}");
            if (channelSent > 0 || userSent > 0 || threadSent > 0)
            {
                StatusText = $"Issue created. Mattermost sent to {channelSent} channel(s), {userSent} user(s), {threadSent} thread(s).";
            }
        }

        private EditorView? GetActiveEditorView()
        {
            return FullscreenEditorViewControl;
        }

        private void UpdateThumbnailFromSnapshot(SkiaSharp.SKBitmap snapshot)
        {
            if (_currentlyEditingImage == null || !_currentlyEditingImage.IsImage)
            {
                return;
            }

            int thumbHeight = Math.Max(1, (int)(snapshot.Height * (double)ThumbnailWidthPixels / snapshot.Width));
            using (var resized = snapshot.Resize(new SkiaSharp.SKImageInfo(ThumbnailWidthPixels, thumbHeight), SkiaSharp.SKFilterQuality.Medium))
            {
                if (resized == null)
                {
                    return;
                }

                using (var thumbImage = SkiaSharp.SKImage.FromBitmap(resized))
                using (var thumbData = thumbImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, ThumbnailQualityPercent))
                using (var ms = new System.IO.MemoryStream())
                {
                    thumbData.SaveTo(ms);
                    ms.Position = 0;
                    _currentlyEditingImage.Thumbnail = new Avalonia.Media.Imaging.Bitmap(ms);
                }
            }
        }

        private async void OnEditorSaveRequested()
        {
            if (_currentlyEditingImage == null) return;

            var activeEditorView = GetActiveEditorView();
            if (activeEditorView == null)
            {
                StatusText = "Save Error: Active editor is not available.";
                return;
            }

            StatusText = "Saving changes...";
            try
            {
                using (var snapshot = activeEditorView.GetSnapshot())
                {
                    if (snapshot != null)
                    {
                        _logger.WriteLine($"Saving image to {_currentlyEditingImage.FilePath}");
                        // Save to disk
                        using (var image = SkiaSharp.SKImage.FromBitmap(snapshot))
                        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                        using (var stream = System.IO.File.OpenWrite(_currentlyEditingImage.FilePath))
                        {
                            stream.SetLength(0); // Clear existing content
                            data.SaveTo(stream);
                        }

                        UpdateThumbnailFromSnapshot(snapshot);

                        EditorViewModel.IsDirty = false;
                        StatusText = $"Saved {_currentlyEditingImage.CaptureType}.";
                    }
                }
            }
            catch (System.Exception ex)
            {
                StatusText = $"Save error: {ex.Message}";
                _logger.WriteException(ex, "Save Error");
            }
        }

        private async void OnEditorCopyRequested()
        {
            StatusText = "Copy: starting...";
            try
            {
                var activeEditorView = GetActiveEditorView();
                if (activeEditorView == null)
                {
                    StatusText = "Copy Error: Active editor is not available.";
                    return;
                }

                using (var snapshot = activeEditorView.GetSnapshot())
                {
                    if (snapshot == null)
                    {
                        StatusText = "Copy Error: GetSnapshot() returned null.";
                        return;
                    }

                    StatusText = $"Copy: Snapshot size {snapshot.Width}x{snapshot.Height}. Encoding...";

                    // Save to a debug file in the project folder for manual verification
                    try
                    {
                        string debugPath = System.IO.Path.Combine(@"d:\DEVERLOPMENT\BugCapture", "debug_copy.png");
                        using (var debugImage = SkiaSharp.SKImage.FromBitmap(snapshot))
                        using (var debugData = debugImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                        using (var fs = System.IO.File.Create(debugPath))
                        {
                            debugData.SaveTo(fs);
                        }
                    }
                    catch { /* Ignore debug save errors */ }

                    using (var image = SkiaSharp.SKImage.FromBitmap(snapshot))
                    using (var pngData = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    {
                        if (pngData == null)
                        {
                            StatusText = "Copy Error: PNG encoding failed.";
                            return;
                        }

                        var bytes = pngData.ToArray();
                        var topLevel = TopLevel.GetTopLevel(this);
                        if (topLevel == null) return;

                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        {
                            StatusText = "Copy: using professional ShareX helpers...";
                            try
                            {
                                using (var ms = new System.IO.MemoryStream(bytes))
                                using (var drawingBmp = new System.Drawing.Bitmap(ms))
                                {
                                    // Set professional options (enables DIB/HTML/PNG combo)
                                    ShareX.HelpersLib.HelpersOptions.UseAlternativeClipboardCopyImage = true;

                                    // Perform copy using ShareX's battle-tested helper
                                    // Passing null for filename to avoid HTML fragment (fixes Excel Ctrl+V)
                                    if (ShareX.HelpersLib.ClipboardHelpers.CopyImage(drawingBmp, null))
                                    {
                                        StatusText = $"Success: Copied {snapshot.Width}x{snapshot.Height} (ShareX)!";
                                        return;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                StatusText = "ShareX copy failed, trying fallbacks...";
                                System.Diagnostics.Debug.WriteLine($"ShareX Copy Error: {ex}");
                            }
                        }

                        // Fallback to Avalonia
                        if (topLevel.Clipboard != null)
                        {
                            var dataObject = new DataObject();
                            dataObject.Set("PNG", bytes);
                            dataObject.Set("image/png", bytes);
                            await topLevel.Clipboard.SetDataObjectAsync(dataObject);
                            StatusText = $"Success: Copied {snapshot.Width}x{snapshot.Height} (Avalonia)!";
                        }
                        else
                        {
                            StatusText = "Copy Error: Clipboard service not found.";
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                StatusText = $"Copy EX: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Copy error: {ex.ToString()}");
            }
        }

        public async void OnCaptureRegionClick(object sender, RoutedEventArgs e)
        {
            _logger.WriteLine("User Clicked: Capture Region");
            this.Hide();
            await System.Threading.Tasks.Task.Delay(250); // Give OS time to hide the window
            try
            {
                var result = await _captureService.CaptureRegion();
                if (result != null)
                {
                    result.CaptureType = $"Evidence_No.{_evidenceCounter++:D2}";
                    CapturedImages.Add(result);

                    // Auto-select the new capture, but don't force show the editor if it was hidden
                    OnThumbnailClickInternal(result, forceShowEditor: false);
                }
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        public async void OnCaptureScrollClick(object sender, RoutedEventArgs e)
        {
            _logger.WriteLine("User Clicked: Capture Scroll");
            this.Hide();
            await System.Threading.Tasks.Task.Delay(250); // Give OS time to hide the window
            try
            {
                var result = await _captureService.CaptureScrolling();
                if (result != null)
                {
                    result.CaptureType = $"Evidence_No.{_evidenceCounter++:D2}";
                    CapturedImages.Add(result);

                    // Auto-select the new capture, but don't force show the editor if it was hidden
                    OnThumbnailClickInternal(result, forceShowEditor: false);
                }
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        public async void OnAddAttachmentClick(object sender, RoutedEventArgs e)
        {
            if (StorageProvider == null)
            {
                StatusText = "Attachment Error: Storage provider unavailable.";
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Select attachments"
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            int addedCount = 0;
            CapturedImage? firstAdded = null;

            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    continue;
                }

                if (CapturedImages.Any(item => string.Equals(item.FilePath, localPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var attachment = CreateAttachmentFromFile(localPath);
                if (attachment == null)
                {
                    continue;
                }

                CapturedImages.Add(attachment);
                addedCount++;
                firstAdded ??= attachment;
            }

            if (firstAdded != null)
            {
                OnThumbnailClickInternal(firstAdded, forceShowEditor: false);
            }

            StatusText = addedCount > 0 ? $"Added {addedCount} attachment(s)." : "No new attachments added.";
        }

        public void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        public void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        public void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == Avalonia.Controls.WindowState.Maximized
                ? Avalonia.Controls.WindowState.Normal
                : Avalonia.Controls.WindowState.Maximized;
        }

        public void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            if (Avalonia.Application.Current != null)
            {
                var isDark = Avalonia.Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

                Avalonia.Application.Current.RequestedThemeVariant =
                    isDark ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;

                // Save setting
                var settings = SettingsService.Load();
                settings.IsDarkMode = !isDark;
                SettingsService.Save(settings);

                OnPropertyChanged(nameof(ThemeIcon));
            }
        }

        public void OnCloseFullscreenEditorClick(object sender, RoutedEventArgs e)
        {
            IsEditorVisible = false;
        }

        public void OnDeleteImageClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CapturedImage image)
            {
                _logger.WriteLine($"User Clicked: Delete Image {image.CaptureType}");
                if (_currentlyEditingImage == image)
                {
                    _currentlyEditingImage = null;
                    EditorViewModel.ClearCommand.Execute(null);
                    EditorViewModel.PreviewImage = null;
                    EditorViewModel.ImageFilePath = null;
                    EditorViewModel.ImageDimensions = "No image";
                    EditorViewModel.IsDirty = false;
                }
                CapturedImages.Remove(image);
            }
        }

        public void OnThumbnailClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Avalonia.Controls.Button btn && btn.DataContext is CapturedImage image)
            {
                OnThumbnailClickInternal(image, forceShowEditor: true);
            }
        }

        public void OnThumbnailPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, sender))
            {
                return;
            }

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (sender is Button btn && btn.DataContext is CapturedImage image)
            {
                _thumbnailDragSource = image;
                _thumbnailDragStartPoint = e.GetPosition(btn);
            }
        }

        public async void OnThumbnailPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_isThumbnailDragInProgress || _thumbnailDragSource == null || _thumbnailDragStartPoint == null)
            {
                return;
            }

            if (sender is not Button btn)
            {
                return;
            }

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            var currentPoint = e.GetPosition(btn);
            var startPoint = _thumbnailDragStartPoint.Value;
            if (Math.Abs(currentPoint.X - startPoint.X) < 4 && Math.Abs(currentPoint.Y - startPoint.Y) < 4)
            {
                return;
            }

            var sourceIndex = CapturedImages.IndexOf(_thumbnailDragSource);
            if (sourceIndex < 0)
            {
                ResetThumbnailDragState();
                return;
            }

            _isThumbnailDragInProgress = true;
            var dragData = new DataObject();
            dragData.Set(DataFormats.Text, $"{ThumbnailDragDataPrefix}{sourceIndex}");

            try
            {
                await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
            }
            finally
            {
                ResetThumbnailDragState();
            }
        }

        public void OnThumbnailDragOver(object? sender, DragEventArgs e)
        {
            var hasInternalDrag = TryGetDraggedThumbnailIndex(e.Data, out _);
            var hasExternalFiles = e.Data.Contains(DataFormats.Files);

            e.DragEffects = (hasInternalDrag || hasExternalFiles)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        public async void OnThumbnailDrop(object? sender, DragEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is CapturedImage targetImage && TryGetDraggedThumbnailIndex(e.Data, out var sourceIndex))
            {
                var targetIndex = CapturedImages.IndexOf(targetImage);
                MoveThumbnail(sourceIndex, targetIndex);
                e.Handled = true;
                return;
            }

            await HandleExternalDropAsync(e);
        }

        public void OnGalleryDragOver(object? sender, DragEventArgs e)
        {
            var hasInternalDrag = TryGetDraggedThumbnailIndex(e.Data, out _);
            var hasExternalFiles = e.Data.Contains(DataFormats.Files);

            e.DragEffects = (hasInternalDrag || hasExternalFiles)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        public async void OnGalleryDrop(object? sender, DragEventArgs e)
        {
            if (TryGetDraggedThumbnailIndex(e.Data, out var sourceIndex))
            {
                MoveThumbnail(sourceIndex, CapturedImages.Count - 1);
                e.Handled = true;
                return;
            }

            await HandleExternalDropAsync(e);
        }

        private Task HandleExternalDropAsync(DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.None;
                e.Handled = true;
                return Task.CompletedTask;
            }

            var droppedItems = e.Data.GetFiles();
            if (droppedItems == null)
            {
                e.Handled = true;
                return Task.CompletedTask;
            }

            int addedCount = 0;
            CapturedImage? firstAdded = null;

            foreach (var item in droppedItems)
            {
                if (item is not IStorageFile file)
                {
                    continue;
                }

                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    continue;
                }

                if (CapturedImages.Any(existing => string.Equals(existing.FilePath, localPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var attachment = CreateAttachmentFromFile(localPath);
                if (attachment == null)
                {
                    continue;
                }

                CapturedImages.Add(attachment);
                addedCount++;
                firstAdded ??= attachment;
            }

            if (firstAdded != null)
            {
                OnThumbnailClickInternal(firstAdded, forceShowEditor: false);
            }

            StatusText = addedCount > 0
                ? $"Added {addedCount} attachment(s) via drag & drop."
                : "No new attachments added from drag & drop.";

            e.DragEffects = addedCount > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return Task.CompletedTask;
        }

        private bool TryGetDraggedThumbnailIndex(IDataObject data, out int sourceIndex)
        {
            sourceIndex = -1;

            var text = data.GetText();
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(ThumbnailDragDataPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(text.Substring(ThumbnailDragDataPrefix.Length), out sourceIndex);
        }

        private void MoveThumbnail(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= CapturedImages.Count || targetIndex < 0 || targetIndex >= CapturedImages.Count)
            {
                return;
            }

            if (sourceIndex == targetIndex)
            {
                return;
            }

            CapturedImages.Move(sourceIndex, targetIndex);
            StatusText = "Thumbnail order updated.";
        }

        private void ResetThumbnailDragState()
        {
            _thumbnailDragStartPoint = null;
            _thumbnailDragSource = null;
            _isThumbnailDragInProgress = false;
        }

        public void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Fullscreen editor only.
        }

        private void OnThumbnailClickInternal(CapturedImage image, bool forceShowEditor = true)
        {
            if (image == null) return;

            // Re-clicking the same opened image should be a no-op.
            if (ReferenceEquals(_currentlyEditingImage, image) && image.IsImage)
            {
                if (!IsEditorVisible)
                {
                    IsEditorVisible = true;
                }
                return;
            }

            // Safety check: only warn if we are actually editing an image and there are changes
            if (_currentlyEditingImage != null && EditorViewModel.IsDirty)
            {
                var result = System.Windows.Forms.MessageBox.Show(
                    "You have unsaved changes on the current image. Do you want to discard them and load a different image?",
                    "Unsaved Changes",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Warning);

                if (result == System.Windows.Forms.DialogResult.No)
                {
                    return;
                }
            }

            // Update selection state for all items
            foreach (var item in CapturedImages)
            {
                item.IsSelected = (item == image);
            }

            if (!image.IsImage)
            {
                _currentlyEditingImage = null;
                IsEditorVisible = false;
                EditorViewModel.ClearCommand.Execute(null);
                EditorViewModel.PreviewImage = null;
                EditorViewModel.ImageFilePath = null;
                EditorViewModel.ImageDimensions = "No image";
                EditorViewModel.IsDirty = false;
                StatusText = $"Selected attachment: {image.CaptureType}";
                return;
            }

            IsEditorVisible = true;

            _currentlyEditingImage = image;
            StatusText = $"Editing {image.CaptureType}...";

            try
            {
                if (System.IO.File.Exists(image.FilePath))
                {
                    using (var stream = System.IO.File.OpenRead(image.FilePath))
                    {
                        var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);

                        // Explicitly clear editor (annotations, history, etc.) before loading new image
                        EditorViewModel.ClearCommand.Execute(null);

                        EditorViewModel.RequestZoomToFitOnNextImageLoad();
                        EditorViewModel.PreviewImage = bitmap;
                        EditorViewModel.LastSavedPath = image.FilePath;
                        EditorViewModel.ImageFilePath = image.FilePath;
                        EditorViewModel.ImageDimensions = $"{bitmap.Size.Width} x {bitmap.Size.Height}";

                        // Reset dirty flag after all internal load-time events (HistoryChanged, etc.) have processed.
                        // Those events often run at Normal priority, so we use Background priority to ensure this is the final word.
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            EditorViewModel.IsDirty = false;
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    }
                }
            }
            catch (System.Exception ex)
            {
                StatusText = $"Error loading editor: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Failed to load image in editor: {ex.Message}");
            }
        }

        private CapturedImage? CreateAttachmentFromFile(string filePath)
        {
            try
            {
                var extension = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant() ?? string.Empty;
                var fileName = System.IO.Path.GetFileName(filePath);
                bool isImage = ImageFileExtensions.Contains(extension);

                var item = new CapturedImage
                {
                    FilePath = filePath,
                    CaptureType = fileName,
                    CapturedAt = DateTime.Now,
                    IsImage = isImage,
                    FileTypeIconData = ResolveFileIconData(extension),
                    FileExtensionDisplay = string.IsNullOrWhiteSpace(extension)
                        ? "FILE"
                        : extension.TrimStart('.').ToUpperInvariant()
                };

                if (isImage)
                {
                    using (var stream = System.IO.File.OpenRead(filePath))
                    {
                        item.Thumbnail = new Avalonia.Media.Imaging.Bitmap(stream);
                    }
                }

                return item;
            }
            catch (Exception ex)
            {
                _logger.WriteException(ex, $"Attachment parse error: {filePath}");
                return null;
            }
        }

        private static string ResolveFileIconData(string extension)
        {
            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return PdfFileIconPathData;
            }

            if (string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".rtf", StringComparison.OrdinalIgnoreCase))
            {
                return WordFileIconPathData;
            }

            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".rar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
            {
                return ArchiveFileIconPathData;
            }

            return GenericFileIconPathData;
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSelections(force: true);
            base.OnClosed(e);
        }
    }
}
