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

            var saveBtn = this.FindControl<Button>("SaveButton");
            var cancelBtn = this.FindControl<Button>("CancelButton");

            if (saveBtn != null) saveBtn.Click += OnSaveClick;
            if (cancelBtn != null) cancelBtn.Click += OnCancelClick;

            CurrentSettings = SettingsService.Load();
            if (urlBox != null) urlBox.Text = CurrentSettings.RedmineUrl;
            if (keyBox != null) keyBox.Text = CurrentSettings.RedmineApiKey;
            if (mattermostUrlBox != null) mattermostUrlBox.Text = CurrentSettings.MattermostServerUrl;
            if (mattermostTokenBox != null) mattermostTokenBox.Text = CurrentSettings.MattermostAccessToken;
            if (autoUpdateEnabledCheckBox != null) autoUpdateEnabledCheckBox.IsChecked = CurrentSettings.AutoUpdateEnabled;
            if (autoUpdateFeedUrlTextBox != null) autoUpdateFeedUrlTextBox.Text = CurrentSettings.AutoUpdateFeedUrl;
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

            if (urlBox == null || keyBox == null || string.IsNullOrWhiteSpace(urlBox.Text) || string.IsNullOrWhiteSpace(keyBox.Text))
            {
                return;
            }

            CurrentSettings.RedmineUrl = urlBox.Text.Trim();
            CurrentSettings.RedmineApiKey = keyBox.Text.Trim();
            CurrentSettings.MattermostServerUrl = mattermostUrlBox?.Text?.Trim() ?? string.Empty;
            CurrentSettings.MattermostAccessToken = mattermostTokenBox?.Text?.Trim() ?? string.Empty;
            CurrentSettings.AutoUpdateEnabled = autoUpdateEnabledCheckBox?.IsChecked ?? true;
            CurrentSettings.AutoUpdateFeedUrl = autoUpdateFeedUrlTextBox?.Text?.Trim() ?? string.Empty;
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
