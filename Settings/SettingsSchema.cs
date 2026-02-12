using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace YASN.Settings
{
    public enum SettingFieldType
    {
        Toggle,
        Text,
        Password,
        Select
    }

    public class SettingOption
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public class SettingField : INotifyPropertyChanged
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public SettingFieldType FieldType { get; set; }
        public bool ShouldSync { get; set; }

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                    OnChanged?.Invoke(this);
                }
            }
        }

        private bool _boolValue;
        public bool BoolValue
        {
            get => _boolValue;
            set
            {
                if (_boolValue != value)
                {
                    _boolValue = value;
                    OnPropertyChanged();
                    OnChanged?.Invoke(this);
                }
            }
        }

        public Action<SettingField> OnChanged { get; set; }
        public ObservableCollection<SettingOption> Options { get; } = new();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SettingAction
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public Func<Task<string>> ExecuteAsync { get; set; }
    }

    public class SettingModule : INotifyPropertyChanged
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public ObservableCollection<SettingField> Fields { get; } = new();
        public ObservableCollection<SettingAction> Actions { get; } = new();

        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SettingsViewModel
    {
        public ObservableCollection<SettingModule> Modules { get; } = new();
    }
}
