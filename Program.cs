using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;

namespace BugCapture
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            StartupDiagnostics.RegisterGlobalHandlers();
            StartupDiagnostics.Write("Process start");
            StartupDiagnostics.Write($"LogPath={StartupDiagnostics.LogFilePath}");
            StartupDiagnostics.Write($"Version={typeof(Program).Assembly.GetName().Version}");
            StartupDiagnostics.Write($"Framework={RuntimeInformation.FrameworkDescription}");
            StartupDiagnostics.Write($"OS={RuntimeInformation.OSDescription}");
            StartupDiagnostics.Write($"ProcessArch={RuntimeInformation.ProcessArchitecture}");
            StartupDiagnostics.Write($"CurrentDirectory={Environment.CurrentDirectory}");
            StartupDiagnostics.Write($"BaseDirectory={AppContext.BaseDirectory}");
            StartupDiagnostics.Write($"Args={string.Join(" ", args ?? Array.Empty<string>())}");

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                StartupDiagnostics.Write("Process exit normally");
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteException("Fatal startup failure", ex);
                throw;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }

    internal static class StartupDiagnostics
    {
        private static readonly object SyncRoot = new object();
        private static bool _handlersRegistered;
        private static string? _logFilePath;

        public static string LogFilePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_logFilePath))
                {
                    return _logFilePath;
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(appData, "BugCapture", "logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, "startup.log");
                return _logFilePath;
            }
        }

        public static void RegisterGlobalHandlers()
        {
            if (_handlersRegistered)
            {
                return;
            }

            _handlersRegistered = true;

            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                if (eventArgs.ExceptionObject is Exception ex)
                {
                    WriteException("AppDomain.CurrentDomain.UnhandledException", ex);
                }
                else
                {
                    Write($"AppDomain.CurrentDomain.UnhandledException: {eventArgs.ExceptionObject}");
                }
            };

            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                WriteException("TaskScheduler.UnobservedTaskException", eventArgs.Exception);
            };
        }

        public static void Write(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (SyncRoot)
                {
                    File.AppendAllText(LogFilePath, line);
                }
            }
            catch
            {
            }
        }

        public static void WriteException(string context, Exception ex)
        {
            Write($"{context}: {ex}");
        }
    }
}
