using System;
using System.ComponentModel;
using BugCapture.Models;

namespace BugCapture.Models
{
    public class CustomFieldControlViewModel : INotifyPropertyChanged
    {
        public RedmineCustomField Field { get; }
        private object? _value;

        public object? Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public bool IsList => Field.FieldFormat == "list";
        public bool IsText => Field.FieldFormat == "string" || Field.FieldFormat == "text";

        public CustomFieldControlViewModel(RedmineCustomField field)
        {
            Field = field;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
