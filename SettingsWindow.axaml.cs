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

            var saveBtn = this.FindControl<Button>("SaveButton");
            var cancelBtn = this.FindControl<Button>("CancelButton");

            if (saveBtn != null) saveBtn.Click += OnSaveClick;
            if (cancelBtn != null) cancelBtn.Click += OnCancelClick;

            CurrentSettings = SettingsService.Load();
            if (urlBox != null) urlBox.Text = CurrentSettings.RedmineUrl;
            if (keyBox != null) keyBox.Text = CurrentSettings.RedmineApiKey;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var urlBox = this.FindControl<TextBox>("UrlTextBox");
            var keyBox = this.FindControl<TextBox>("ApiKeyTextBox");

            if (urlBox == null || keyBox == null || string.IsNullOrWhiteSpace(urlBox.Text) || string.IsNullOrWhiteSpace(keyBox.Text))
            {
                return;
            }

            CurrentSettings.RedmineUrl = urlBox.Text.Trim();
            CurrentSettings.RedmineApiKey = keyBox.Text.Trim();
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
