using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BugCapture.Models;
using BugCapture.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ShareX.ImageEditor.Presentation.Views;
using ShareX.ImageEditor.Presentation.ViewModels;
using Avalonia.Input;
using System.Collections.Generic;
using System.Net.Http;
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
        private bool _isEditorVisible = true;
        private double _expandedHeight = 850;
        private string _currentDateTime = string.Empty;
        private System.Threading.Timer? _clockTimer;
        private bool _isSynchronizingEditors;
        private EditorView? _lastInteractedEditor;
        private readonly Logger _logger = new Logger();

        // Redmine Integration
        private readonly RedmineService _redmineService;
        private const int ProjectIndentSpacesPerLevel = 2;
        private const int RootParentProjectId = 0;
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
                _isEditorVisible = value;
                OnPropertyChanged(nameof(IsEditorVisible));

                if (value)
                {
                    // Restore expanded height
                    Height = _expandedHeight;
                }
                else
                {
                    // Save current height before collapsing
                    _expandedHeight = Height;
                    // Let Avalonia auto-calculate compact height, then lock to Manual
                    SizeToContent = Avalonia.Controls.SizeToContent.Height;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        SizeToContent = Avalonia.Controls.SizeToContent.Manual;
                    }, Avalonia.Threading.DispatcherPriority.Render);
                }
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

        private void SaveSelections()
        {
            // Don't save if we're still initializing
            if (!_isInitialized) return;

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
            EditorViewModel.PropertyChanged += OnEditorViewModelPropertyChanged;

            // Start live clock
            UpdateClock(null);
            _clockTimer = new System.Threading.Timer(UpdateClock, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            DataContext = this;

            _redmineService = new RedmineService();
            // Load initial settings
            var settings = SettingsService.Load();
            RedmineUrl = settings.RedmineUrl;
            RedmineApiKey = settings.RedmineApiKey;

            // Apply Theme
            if (Avalonia.Application.Current != null)
            {
                Avalonia.Application.Current.RequestedThemeVariant = 
                    settings.IsDarkMode ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
            }

            _bugPrefix = settings.LastIssuePrefix;
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
                _ = LoadRedmineData();
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
            Memberships.Clear();

            if (SelectedProject != null)
            {
                var trackers = await _redmineService.GetTrackersForProjectAsync(SelectedProject.Identifier);
                Trackers = new ObservableCollection<ProjectTracker>(trackers);

                var members = await _redmineService.GetMembershipsAsync(SelectedProject.Identifier, SelectedProject.Id);
                Memberships = new ObservableCollection<ProjectMembership>(members.Where(m => m.User != null));

                // Restore Selections
                var settings = SettingsService.Load();
                if (settings.LastTrackerId.HasValue)
                {
                    SelectedTracker = Trackers.FirstOrDefault(t => t.Id == settings.LastTrackerId.Value);
                }
                if (SelectedTracker == null) SelectedTracker = Trackers.FirstOrDefault();

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

            IsSubmitting = true;
            StatusText = "Uploading images...";

            try
            {
                var uploads = new List<Upload>();
                foreach (var img in CapturedImages)
                {
                    var upload = await _redmineService.UploadFileAsync(img.FilePath);
                    if (upload != null) uploads.Add(upload);
                }

                var finalDescription = BugDescription;
                if (uploads.Count > 0)
                {
                    finalDescription += "\n\n" + (System.Globalization.CultureInfo.CurrentCulture.Name.StartsWith("vi") ? "--- Bằng chứng (Evidence): ---" : "--- Evidence: ---") + "\n";
                    foreach (var upload in uploads)
                    {
                        finalDescription += $"\n!{upload.FileName}!";
                    }
                }

                var issue = new Issue
                {
                    Project = IdentifiableName.Create<Project>(SelectedProject.Id),
                    Tracker = IdentifiableName.Create<Tracker>(SelectedTracker.Id),
                    Status = IdentifiableName.Create<IssueStatus>(_defaultStatusId),
                    Priority = IdentifiableName.Create<IssuePriority>(2), // Normal
                    Subject = string.IsNullOrWhiteSpace(BugPrefix) ? BugTitle : $"[{BugPrefix}] {BugTitle}",
                    Description = finalDescription,
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
                var success = await _redmineService.CreateIssueAsync(issue);

                if (success)
                {
                    StatusText = "Issue created successfully!";
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
                    StatusText = "Failed to create Redmine issue.";
                }
            }
            catch (Exception ex)
            {
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

        private EditorView? GetActiveEditorView()
        {
            if (_lastInteractedEditor != null)
            {
                return _lastInteractedEditor;
            }

            if (EditorViewModel.IsEditorMaximized)
            {
                return FullscreenEditorViewControl;
            }

            return EditorViewControl;
        }

        private EditorView? GetInactiveEditorView()
        {
            var activeEditor = GetActiveEditorView();
            if (ReferenceEquals(activeEditor, EditorViewControl))
            {
                return FullscreenEditorViewControl;
            }

            return EditorViewControl;
        }

        private void UpdateThumbnailFromSnapshot(SkiaSharp.SKBitmap snapshot)
        {
            if (_currentlyEditingImage == null)
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

        private void SyncEditors(bool updateThumbnail)
        {
            if (_isSynchronizingEditors || _currentlyEditingImage == null)
            {
                return;
            }

            var activeEditorView = GetActiveEditorView();
            var inactiveEditorView = GetInactiveEditorView();
            if (activeEditorView == null || inactiveEditorView == null)
            {
                return;
            }

            try
            {
                _isSynchronizingEditors = true;
                using (var snapshot = activeEditorView.GetSnapshot())
                {
                    if (snapshot == null)
                    {
                        return;
                    }

                    inactiveEditorView.LoadSnapshot(snapshot);
                    if (updateThumbnail)
                    {
                        UpdateThumbnailFromSnapshot(snapshot);
                    }
                }
            }
            finally
            {
                _isSynchronizingEditors = false;
            }
        }

        private void OnEditorViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditorViewModel.IsEditorMaximized))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SyncEditors(updateThumbnail: true);
                }, Avalonia.Threading.DispatcherPriority.Background);
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

                        var inactiveEditorView = GetInactiveEditorView();
                        if (inactiveEditorView != null)
                        {
                            inactiveEditorView.LoadSnapshot(snapshot);
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

        public void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is EditorView view)
            {
                _lastInteractedEditor = view;
            }
        }

        private void OnThumbnailClickInternal(CapturedImage image, bool forceShowEditor = true)
        {
            if (image == null) return;

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

            // Show editor only if forced (manual click) or already visible
            if (forceShowEditor)
            {
                IsEditorVisible = true;
            }

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
                        _lastInteractedEditor = EditorViewControl;

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

        protected override void OnClosed(EventArgs e)
        {
            EditorViewModel.PropertyChanged -= OnEditorViewModelPropertyChanged;
            base.OnClosed(e);
        }
    }
}
