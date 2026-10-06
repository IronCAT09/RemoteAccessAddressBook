using System.Windows.Media;

namespace RemoteAccessAddressBook.ViewModels
{
    /// <summary>Иконка программы над таблицей: найдена в системе или нет.</summary>
    public class ToolStatusItem : ViewModelBase
    {
        private ImageSource _icon;
        private bool _isAvailable;
        private string _exePath = string.Empty;

        public ToolStatusItem(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public string Key { get; }

        public string DisplayName { get; }

        /// <summary>Иконка из exe; null — берётся запасной значок с первой буквой названия.</summary>
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                if (Set(ref _icon, value))
                {
                    OnPropertyChanged(nameof(HasIcon));
                    OnPropertyChanged(nameof(FallbackText));
                }
            }
        }

        public bool HasIcon => _icon != null;

        /// <summary>Заглушка вместо иконки, если извлечь её не удалось.</summary>
        public string FallbackText => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName.Substring(0, 1);

        /// <summary>Программа найдена на компьютере — иконка показывается ярко.</summary>
        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (Set(ref _isAvailable, value))
                {
                    OnPropertyChanged(nameof(IconOpacity));
                    OnPropertyChanged(nameof(ToolTipText));
                }
            }
        }

        public string ExePath
        {
            get => _exePath;
            set
            {
                if (Set(ref _exePath, value))
                {
                    OnPropertyChanged(nameof(ToolTipText));
                }
            }
        }

        public double IconOpacity => IsAvailable ? 1.0 : 0.35;

        public string ToolTipText => IsAvailable
            ? DisplayName + " — найден:\n" + ExePath + "\n\nКлик — запустить программу."
            : DisplayName + " — не найден на компьютере, запуск недоступен.\n" +
              "Откройте настройки, чтобы указать путь, или нажмите «Найти установленные».";
    }
}
