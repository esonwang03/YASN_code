using System;
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
        private string _titleBarColor;
        private string _backgroundImagePath;
        private double _backgroundImageOpacity;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
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
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged(nameof(Content));
                }
            }
        }

        public WindowLevel Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged(nameof(Level));
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(LevelPrefix));
                }
            }
        }

        public double Left
        {
            get => _left;
            set
            {
                if (Math.Abs(_left - value) > 0.01)
                {
                    _left = value;
                    OnPropertyChanged(nameof(Left));
                }
            }
        }

        public double Top
        {
            get => _top;
            set
            {
                if (Math.Abs(_top - value) > 0.01)
                {
                    _top = value;
                    OnPropertyChanged(nameof(Top));
                }
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (Math.Abs(_width - value) > 0.01)
                {
                    _width = value;
                    OnPropertyChanged(nameof(Width));
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) > 0.01)
                {
                    _height = value;
                    OnPropertyChanged(nameof(Height));
                }
            }
        }

        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen != value)
                {
                    _isOpen = value;
                    OnPropertyChanged(nameof(IsOpen));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText => IsOpen ? "Open" : "Closed";

        public FloatingWindow Window { get; set; }

        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged(nameof(IsEditMode));
                }
            }
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnPropertyChanged(nameof(IsDarkMode));
                }
            }
        }

        public string TitleBarColor
        {
            get => _titleBarColor ?? "#E6D4C5E0";
            set
            {
                if (_titleBarColor != value)
                {
                    _titleBarColor = value;
                    OnPropertyChanged(nameof(TitleBarColor));
                }
            }
        }

        public string BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                if (_backgroundImagePath != value)
                {
                    _backgroundImagePath = value;
                    OnPropertyChanged(nameof(BackgroundImagePath));
                }
            }
        }

        public double BackgroundImageOpacity
        {
            get => _backgroundImageOpacity > 0 ? _backgroundImageOpacity : 0.15;
            set
            {
                if (Math.Abs(_backgroundImageOpacity - value) > 0.01)
                {
                    _backgroundImageOpacity = value;
                    OnPropertyChanged(nameof(BackgroundImageOpacity));
                }
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
