using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using BugCapture.Models;
using BugCapture.Services;

namespace BugCapture
{
    public partial class SettingsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }
        public bool IsSaved { get; private set; } = false;

        public SettingsWindow()
        {
            InitializeComponent();

            var urlBox = this.FindControl<TextBox>("UrlTextBox");
            var keyBox = this.FindControl<TextBox>("ApiKeyTextBox");
            var mattermostUrlBox = this.FindControl<TextBox>("MattermostUrlTextBox");
            var mattermostTokenBox = this.FindControl<TextBox>("MattermostTokenTextBox");
            var autoUpdateEnabledCheckBox = this.FindControl<CheckBox>("AutoUpdateEnabledCheckBox");
            var autoUpdateFeedUrlTextBox = this.FindControl<TextBox>("AutoUpdateFeedUrlTextBox");
            var openEditorAfterCaptureCheckBox = this.FindControl<CheckBox>("OpenEditorAfterCaptureCheckBox");

            var saveBtn = this.FindControl<Button>("SaveButton");
            var cancelBtn = this.FindControl<Button>("CancelButton");

            if (saveBtn != null) saveBtn.Click += OnSaveClick;
            if (cancelBtn != null) cancelBtn.Click += OnCancelClick;

            CurrentSettings = SettingsService.Load();
            if (string.IsNullOrWhiteSpace(CurrentSettings.AutoUpdateFeedUrl))
            {
                CurrentSettings.AutoUpdateFeedUrl = AppSettings.DefaultAutoUpdateFeedUrl;
            }
            if (urlBox != null) urlBox.Text = CurrentSettings.RedmineUrl;
            if (keyBox != null) keyBox.Text = CurrentSettings.RedmineApiKey;
            if (mattermostUrlBox != null) mattermostUrlBox.Text = CurrentSettings.MattermostServerUrl;
            if (mattermostTokenBox != null)
            {
                mattermostTokenBox.Text = string.IsNullOrWhiteSpace(CurrentSettings.MattermostBotAccessToken)
                    ? CurrentSettings.MattermostAccessToken
                    : CurrentSettings.MattermostBotAccessToken;
            }
            if (autoUpdateEnabledCheckBox != null) autoUpdateEnabledCheckBox.IsChecked = CurrentSettings.AutoUpdateEnabled;
            if (autoUpdateFeedUrlTextBox != null) autoUpdateFeedUrlTextBox.Text = CurrentSettings.AutoUpdateFeedUrl;
            if (openEditorAfterCaptureCheckBox != null) openEditorAfterCaptureCheckBox.IsChecked = CurrentSettings.OpenEditorAfterCapture;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var urlBox = this.FindControl<TextBox>("UrlTextBox");
            var keyBox = this.FindControl<TextBox>("ApiKeyTextBox");
            var mattermostUrlBox = this.FindControl<TextBox>("MattermostUrlTextBox");
            var mattermostTokenBox = this.FindControl<TextBox>("MattermostTokenTextBox");
            var autoUpdateEnabledCheckBox = this.FindControl<CheckBox>("AutoUpdateEnabledCheckBox");
            var autoUpdateFeedUrlTextBox = this.FindControl<TextBox>("AutoUpdateFeedUrlTextBox");
            var openEditorAfterCaptureCheckBox = this.FindControl<CheckBox>("OpenEditorAfterCaptureCheckBox");

            if (urlBox == null || keyBox == null || string.IsNullOrWhiteSpace(urlBox.Text) || string.IsNullOrWhiteSpace(keyBox.Text))
            {
                return;
            }

            CurrentSettings.RedmineUrl = urlBox.Text.Trim();
            CurrentSettings.RedmineApiKey = keyBox.Text.Trim();
            CurrentSettings.MattermostServerUrl = mattermostUrlBox?.Text?.Trim() ?? string.Empty;
            var mattermostBotToken = mattermostTokenBox?.Text?.Trim() ?? string.Empty;
            CurrentSettings.MattermostBotAccessToken = mattermostBotToken;
            CurrentSettings.MattermostAccessToken = mattermostBotToken;
            CurrentSettings.AutoUpdateEnabled = autoUpdateEnabledCheckBox?.IsChecked ?? true;
            var autoUpdateFeedUrl = autoUpdateFeedUrlTextBox?.Text?.Trim() ?? string.Empty;
            CurrentSettings.AutoUpdateFeedUrl = string.IsNullOrWhiteSpace(autoUpdateFeedUrl)
                ? AppSettings.DefaultAutoUpdateFeedUrl
                : autoUpdateFeedUrl;
            CurrentSettings.OpenEditorAfterCapture = openEditorAfterCaptureCheckBox?.IsChecked ?? false;
            SettingsService.Save(CurrentSettings);
            IsSaved = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
