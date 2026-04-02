using Avalonia.Media.Imaging;
using System;
using System.ComponentModel;

namespace BugCapture.Models
{
    public class CapturedImage : INotifyPropertyChanged
    {
        private Bitmap? _thumbnail;
        private string _captureType = string.Empty;
        private string _fileTypeIconData = string.Empty;
        private string _fileExtensionDisplay = string.Empty;

        private bool _isSelected;
        public string FilePath { get; set; } = string.Empty;
        public bool IsImage { get; set; } = true;
        
        public Bitmap? Thumbnail 
        { 
            get => _thumbnail; 
            set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); } 
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public DateTime CapturedAt { get; set; } = DateTime.Now;

        public string CaptureType 
        { 
            get => _captureType; 
            set { _captureType = value; OnPropertyChanged(nameof(CaptureType)); } 
        }

        public string FileTypeIconData
        {
            get => _fileTypeIconData;
            set { _fileTypeIconData = value; OnPropertyChanged(nameof(FileTypeIconData)); }
        }

        public string FileExtensionDisplay
        {
            get => _fileExtensionDisplay;
            set { _fileExtensionDisplay = value; OnPropertyChanged(nameof(FileExtensionDisplay)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
