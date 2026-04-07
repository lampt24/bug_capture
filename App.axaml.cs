using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;

namespace BugCapture
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            StartupDiagnostics.Write("App.Initialize start");
            AvaloniaXamlLoader.Load(this);
            StartupDiagnostics.Write("App.Initialize done");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            StartupDiagnostics.Write("OnFrameworkInitializationCompleted start");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    desktop.MainWindow = new MainWindow();
                    StartupDiagnostics.Write("MainWindow created");
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.WriteException("MainWindow creation failed", ex);
                    throw;
                }
            }

            base.OnFrameworkInitializationCompleted();
            StartupDiagnostics.Write("OnFrameworkInitializationCompleted done");
        }

        private void OnTrayIconClicked(object? sender, System.EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.Show();
                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                    desktop.MainWindow.Activate();
                }
            }
        }

        private void OnOpenClicked(object? sender, System.EventArgs e)
        {
            OnTrayIconClicked(sender, e);
        }

        private void OnExitClicked(object? sender, System.EventArgs e)
        {
            StartupDiagnostics.Write("Tray Exit clicked");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}