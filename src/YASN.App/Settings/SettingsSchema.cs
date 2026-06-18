using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YASN.App.Settings
{
    public enum SettingFieldType
    {
        Toggle,
        Text,
        Password,
        Select,
        Number,
        Hotkey
    }

    public class SettingOption
    {
        public string? Label { get; set; }
        public string? Value { get; set; }
    }

    public class SettingField : INotifyPropertyChanged
    {
        private string _value = string.Empty;
        public string? Key { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public SettingFieldType FieldType { get; set; }
        public bool ShouldSync { get; set; }
        public bool EnableFolderBrowse { get; set; }

        /// <summary>
        /// Gets or sets the factory-default value, used by the reset button on hotkey fields.
        /// </summary>
        public string DefaultValue { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the inclusive minimum for <see cref="SettingFieldType.Number"/> fields.
        /// </summary>
        public decimal Minimum { get; set; } = decimal.MinValue;

        /// <summary>
        /// Gets or sets the inclusive maximum for <see cref="SettingFieldType.Number"/> fields.
        /// </summary>
        public decimal Maximum { get; set; } = decimal.MaxValue;

        public string Value
        {
            get => _value;
            set
            {
                string nextValue = value ?? string.Empty;

                if (_value != nextValue)
                {
                    _value = nextValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectValue));
                    OnChanged?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// Gets or sets the selection for <see cref="SettingFieldType.Select"/> fields. This is a
        /// typed projection over <see cref="Value"/>, mirroring how <see cref="NumberValue"/> and
        /// <see cref="BoolValue"/> project for their field types. The setter only writes back when
        /// this is a Select field, so a hidden <c>ComboBox</c> with an empty option list cannot
        /// coerce its selection to null and clobber a Text/Password field's <see cref="Value"/>.
        /// </summary>
        public string? SelectValue
        {
            get => _value;
            set
            {
                if (FieldType != SettingFieldType.Select)
                {
                    return;
                }

                Value = value ?? string.Empty;
            }
        }

        private bool _boolValue;
        public bool BoolValue
        {
            get => _boolValue;
            set
            {
                if (_boolValue == value) return;
                _boolValue = value;
                OnPropertyChanged();
                OnChanged?.Invoke(this);
            }
        }

        public Action<SettingField>? OnChanged { get; set; }
        public ObservableCollection<SettingOption> Options { get; } = new();

        /// <summary>
        /// Gets or sets the numeric view of <see cref="Value"/> for <see cref="SettingFieldType.Number"/>
        /// fields. Setting clamps to <see cref="Minimum"/>/<see cref="Maximum"/> and writes the
        /// canonical string into <see cref="Value"/>.
        /// </summary>
        public decimal NumberValue
        {
            get => decimal.TryParse(_value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : 0m;
            set
            {
                decimal clamped = value;
                if (clamped < Minimum)
                {
                    clamped = Minimum;
                }

                if (clamped > Maximum)
                {
                    clamped = Maximum;
                }

                Value = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SettingAction
    {
        public string? Key { get; set; }
        public string? Label { get; set; }
        public Func<Task<string>>? ExecuteAsync { get; set; }
    }

    public class SettingModule : INotifyPropertyChanged
    {
        private string _status = string.Empty;
        public string? Key { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public ObservableCollection<SettingField> Fields { get; } = new();
        public ObservableCollection<SettingAction> Actions { get; } = new();

        public string Status
        {
            get => _status;
            set
            {
                string nextStatus = value ?? string.Empty;

                if (_status != nextStatus)
                {
                    _status = nextStatus;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SettingsViewModel
    {
        public ObservableCollection<SettingModule> Modules { get; } = new();
    }
}
