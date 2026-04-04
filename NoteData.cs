using System.ComponentModel;

namespace YASN
{
    public class NoteData : INotifyPropertyChanged
    {
        private int _id;
        private string _title;
        private string _content;
        private WindowLevel _level;
        private double _left;
        private double _top;
        private double _width;
        private double _height;
        private bool _isOpen;
        private bool _isEditMode;
        private bool _isDarkMode;
        private EditorDisplayMode? _lastEditorDisplayMode;
        private string _titleBarColor;
        private string _backgroundImagePath;
        private double _backgroundImageOpacity;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged(nameof(Id));
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        public string DisplayTitle => $"{LevelPrefix}{Title}";

        public string LevelPrefix => Level switch
        {
            WindowLevel.TopMost => "[T] ",
            WindowLevel.BottomMost => "[B] ",
            _ => ""
        };

        public string Content
        {
            get => _content;
            set
            {
                if (_content == value) return;
                _content = value;
                OnPropertyChanged(nameof(Content));
            }
        }

        public WindowLevel Level
        {
            get => _level;
            set
            {
                if (_level == value) return;
                _level = value;
                OnPropertyChanged(nameof(Level));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(LevelPrefix));
            }
        }

        public double Left
        {
            get => _left;
            set
            {
                if (!(Math.Abs(_left - value) > 0.01)) return;
                _left = value;
                OnPropertyChanged(nameof(Left));
            }
        }

        public double Top
        {
            get => _top;
            set
            {
                if (!(Math.Abs(_top - value) > 0.01)) return;
                _top = value;
                OnPropertyChanged(nameof(Top));
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (!(Math.Abs(_width - value) > 0.01)) return;
                _width = value;
                OnPropertyChanged(nameof(Width));
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (!(Math.Abs(_height - value) > 0.01)) return;
                _height = value;
                OnPropertyChanged(nameof(Height));
            }
        }

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value) return;
                _isOpen = value;
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => IsOpen ? "Open" : "Closed";

        public FloatingWindow Window { get; set; }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode == value) return;
                _isEditMode = value;
                OnPropertyChanged(nameof(IsEditMode));
            }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode == value) return;
                _isDarkMode = value;
                OnPropertyChanged(nameof(IsDarkMode));
            }
        }

        public EditorDisplayMode? LastEditorDisplayMode
        {
            get => _lastEditorDisplayMode;
            set
            {
                if (_lastEditorDisplayMode == value) return;
                _lastEditorDisplayMode = value;
                OnPropertyChanged(nameof(LastEditorDisplayMode));
            }
        }

        public string TitleBarColor
        {
            get => _titleBarColor ?? "#E6D4C5E0";
            set
            {
                if (_titleBarColor == value) return;
                _titleBarColor = value;
                OnPropertyChanged(nameof(TitleBarColor));
            }
        }

        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                if (_backgroundImagePath == value) return;
                _backgroundImagePath = value;
                OnPropertyChanged(nameof(BackgroundImagePath));
            }
        }

        public double BackgroundImageOpacity
        {
            get => _backgroundImageOpacity > 0 ? _backgroundImageOpacity : 0.15;
            set
            {
                if (!(Math.Abs(_backgroundImageOpacity - value) > 0.01)) return;
                _backgroundImageOpacity = value;
                OnPropertyChanged(nameof(BackgroundImageOpacity));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
