using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemouteAddressBook.Models
{
    /// <summary>Метка (тег). У контакта может быть несколько меток.</summary>
    public class LabelItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _isChecked;

        public LabelItem(long id, string name)
        {
            Id = id;
            _name = name;
        }

        public long Id { get; }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Отметка в окне редактирования контакта (мультивыбор меток).</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
