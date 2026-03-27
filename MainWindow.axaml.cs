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

namespace BugCapture
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly ShareXCaptureService _captureService;
        private int _evidenceCounter = 1;
        private string _statusText = "Ready";
        private string _bugTitle = string.Empty;
        private string _bugDescription = string.Empty;
        private CapturedImage? _currentlyEditingImage;
        private bool _isEditorVisible = true;
        private double _expandedHeight = 850;

        public ObservableCollection<CapturedImage> CapturedImages { get; } = new ObservableCollection<CapturedImage>();
        public MainViewModel EditorViewModel { get; } = new MainViewModel();

        public string BugTitle
        {
            get => _bugTitle;
            set { _bugTitle = value; OnPropertyChanged(nameof(BugTitle)); }
        }

        public string BugDescription
        {
            get => _bugDescription;
            set { _bugDescription = value; OnPropertyChanged(nameof(BugDescription)); }
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

        public MainWindow()
        {
            InitializeComponent();
            _captureService = new ShareXCaptureService();
            EditorViewModel.ShowTaskModeButtons = false;
            
            // Subscribe to editor events
            EditorViewModel.SaveRequested += OnEditorSaveRequested;
            EditorViewModel.CopyRequested += OnEditorCopyRequested;

            DataContext = this;
        }

        private async void OnEditorSaveRequested()
        {
            if (_currentlyEditingImage == null) return;

            StatusText = "Saving changes...";
            try
            {
                using (var snapshot = EditorViewControl.GetSnapshot())
                {
                    if (snapshot != null)
                    {
                        // Save to disk
                        using (var image = SkiaSharp.SKImage.FromBitmap(snapshot))
                        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                        using (var stream = System.IO.File.OpenWrite(_currentlyEditingImage.FilePath))
                        {
                            stream.SetLength(0); // Clear existing content
                            data.SaveTo(stream);
                        }

                        // Update Thumbnail in UI
                        // Create a small version for thumbnail
                        int thumbWidth = 100;
                        int thumbHeight = (int)(snapshot.Height * (double)thumbWidth / snapshot.Width);
                        using (var resized = snapshot.Resize(new SkiaSharp.SKImageInfo(thumbWidth, thumbHeight), SkiaSharp.SKFilterQuality.Medium))
                        using (var thumbImage = SkiaSharp.SKImage.FromBitmap(resized))
                        using (var thumbData = thumbImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 80))
                        using (var ms = new System.IO.MemoryStream())
                        {
                            thumbData.SaveTo(ms);
                            ms.Position = 0;
                            _currentlyEditingImage.Thumbnail = new Avalonia.Media.Imaging.Bitmap(ms);
                        }

                        EditorViewModel.IsDirty = false;
                        StatusText = $"Saved {_currentlyEditingImage.CaptureType}.";
                    }
                }
            }
            catch (System.Exception ex)
            {
                StatusText = $"Save error: {ex.Message}";
            }
        }

        private async void OnEditorCopyRequested()
        {
            StatusText = "Copy: starting...";
            try
            {
                if (EditorViewControl == null)
                {
                    StatusText = "Copy Error: EditorViewControl is null.";
                    return;
                }

                using (var snapshot = EditorViewControl.GetSnapshot())
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
                    } catch { /* Ignore debug save errors */ }

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
            var result = await _captureService.CaptureRegion();
            if (result != null)
            {
                result.CaptureType = $"Evidence_No.{_evidenceCounter++:D2}";
                CapturedImages.Add(result);
                
                // Auto-select the new capture
                OnThumbnailClick(result);
            }
        }

        public async void OnCaptureScrollClick(object sender, RoutedEventArgs e)
        {
            var result = await _captureService.CaptureScrolling();
            if (result != null)
            {
                result.CaptureType = $"Evidence_No.{_evidenceCounter++:D2}";
                CapturedImages.Add(result);

                // Auto-select the new capture
                OnThumbnailClick(result);
            }
        }

        public void OnDeleteImageClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CapturedImage image)
            {
                if (_currentlyEditingImage == image)
                {
                    _currentlyEditingImage = null;
                    EditorViewModel.ClearCommand.Execute(null);
                }
                CapturedImages.Remove(image);
            }
        }

        public void OnThumbnailClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is CapturedImage image)
            {
                OnThumbnailClick(image);
            }
        }

        private void OnThumbnailClick(CapturedImage image)
        {
            // Safety check: confirm if switching while having unsaved changes
            if (EditorViewModel.IsDirty)
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
        }
    }
