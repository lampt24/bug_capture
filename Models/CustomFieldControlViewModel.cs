using System;
using System.ComponentModel;
using System.Linq;
using Redmine.Net.Api.Types;

namespace BugCapture.Models
{
    public class CustomFieldControlViewModel : INotifyPropertyChanged
    {
        public CustomField Field { get; }
        public string DisplayName => Field.Name?.ToUpper() ?? string.Empty;
        private object? _value;

        public object? Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
                OnPropertyChanged(nameof(NumericValue));
                OnPropertyChanged(nameof(DateValue));
                OnPropertyChanged(nameof(BoolValue));
                OnPropertyChanged(nameof(ListSelectedItem));
            }
        }

        public CustomFieldPossibleValue? ListSelectedItem
        {
            get
            {
                if (_value == null || Field.PossibleValues == null) return null;
                return Field.PossibleValues.FirstOrDefault(p => p.Value == _value.ToString());
            }
            set => Value = value?.Value;
        }

        public decimal? NumericValue
        {
            get
            {
                if (_value != null && decimal.TryParse(_value.ToString(), out decimal res)) return res;
                return null;
            }
            set => Value = value?.ToString();
        }

        public DateTimeOffset? DateValue
        {
            get
            {
                if (_value != null && DateTimeOffset.TryParse(_value.ToString(), out DateTimeOffset res)) return res;
                return null;
            }
            set => Value = value?.ToString("yyyy-MM-dd");
        }

        public bool BoolValue
        {
            get => _value?.ToString() == "1" || _value?.ToString()?.ToLower() == "true";
            set => Value = value ? "1" : "0";
        }

        public bool IsList => Field.FieldFormat == "list";
        public bool IsText => Field.FieldFormat == "string" || Field.FieldFormat == "text" || Field.FieldFormat == "link";
        public bool IsNumber => Field.FieldFormat == "int" || Field.FieldFormat == "float";
        public bool IsDate => Field.FieldFormat == "date";
        public bool IsBool => Field.FieldFormat == "bool";

        public CustomFieldControlViewModel(CustomField field)
        {
            Field = field;
            if (field.FieldFormat == "list" && field.PossibleValues != null && field.PossibleValues.Count > 0)
            {
                var def = field.DefaultValue;
                var match = field.PossibleValues.FirstOrDefault(p => p.Value == def);
                Value = match ?? field.PossibleValues[0];
            }
            else if (!string.IsNullOrEmpty(field.DefaultValue))
            {
                Value = field.DefaultValue;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
