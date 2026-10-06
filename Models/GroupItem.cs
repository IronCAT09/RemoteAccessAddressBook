using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteAccessAddressBook.Models
{
    public enum GroupKind
    {
        /// <summary>Служебный пункт «Все».</summary>
        All,

        /// <summary>Служебный пункт «Без группы».</summary>
        Ungrouped,

        /// <summary>Обычная пользовательская группа.</summary>
        Normal,
    }

    /// <summary>Элемент списка групп (включая служебные пункты).</summary>
    public class GroupItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _count;

        public GroupItem(long id, string name, GroupKind kind)
        {
            Id = id;
            _name = name;
            Kind = kind;
        }

        public long Id { get; }

        public GroupKind Kind { get; }

        public bool IsSystem => Kind != GroupKind.Normal;

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
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Title));
            }
        }

        public int Count
        {
            get => _count;
            set
            {
                if (_count == value)
                {
                    return;
                }

                _count = value;
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Title => Name + " (" + Count + ")";

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
