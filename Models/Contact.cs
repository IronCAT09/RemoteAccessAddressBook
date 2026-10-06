using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteAccessAddressBook.Models
{
    /// <summary>Откуда контакт: локальная/сетевая база или облачная (сервер).</summary>
    public enum ContactSource
    {
        Local,
        Cloud,
    }

    /// <summary>Контакт адресной книги.</summary>
    public class Contact : INotifyPropertyChanged
    {
        private ContactSource _source;
        private string _cloudId = string.Empty;
        private long _version;
        private string _groupName = string.Empty;
        private bool _hasConflict;
        private long _id;
        private string _name = string.Empty;
        private string _comment = string.Empty;
        private string _anyDesk = string.Empty;
        private string _rudesktop = string.Empty;
        private string _assistant = string.Empty;
        private string _ammyy = string.Empty;
        private string _rdp = string.Empty;
        private string _password = string.Empty;
        private string _rdpLogin = string.Empty;
        private string _rdpPassword = string.Empty;
        private long? _groupId;

        public ContactSource Source
        {
            get => _source;
            set
            {
                if (Set(ref _source, value))
                {
                    OnPropertyChanged(nameof(IsCloud));
                    OnPropertyChanged(nameof(Key));
                    OnPropertyChanged(nameof(SourceGlyph));
                    OnPropertyChanged(nameof(SourceToolTip));
                }
            }
        }

        public bool IsCloud => _source == ContactSource.Cloud;

        /// <summary>Id записи на сервере (GUID) для облачных контактов.</summary>
        public string CloudId
        {
            get => _cloudId;
            set
            {
                if (Set(ref _cloudId, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        /// <summary>Id строки в локальной базе (для локальных контактов).</summary>
        public long Id
        {
            get => _id;
            set
            {
                if (Set(ref _id, value))
                {
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        /// <summary>Уникальный ключ контакта среди обеих баз.</summary>
        public string Key => IsCloud ? "C:" + _cloudId : "L:" + _id;

        /// <summary>Версия записи: для проверки, не изменил ли её кто-то ещё, пока она была открыта.</summary>
        public long Version
        {
            get => _version;
            set => Set(ref _version, value);
        }

        /// <summary>Имя группы. Группы обеих баз сопоставляются по имени (без учёта регистра).</summary>
        public string GroupName
        {
            get => _groupName;
            set => Set(ref _groupName, value ?? string.Empty);
        }

        /// <summary>Имена меток. Метки обеих баз сопоставляются по имени.</summary>
        public HashSet<string> LabelNames { get; } = new HashSet<string>(System.StringComparer.CurrentCultureIgnoreCase);

        /// <summary>Есть такой же контакт в другой базе, но с другими данными.</summary>
        public bool HasConflict
        {
            get => _hasConflict;
            set
            {
                if (Set(ref _hasConflict, value))
                {
                    OnPropertyChanged(nameof(SourceGlyph));
                    OnPropertyChanged(nameof(SourceToolTip));
                }
            }
        }

        /// <summary>Значок в колонке «Где»: облако, локальная база или предупреждение о расхождении.</summary>
        public string SourceGlyph => HasConflict ? "" : IsCloud ? "" : "";

        public string SourceToolTip
        {
            get
            {
                var where = IsCloud ? "Облачная база" : "Локальная база";
                return HasConflict
                    ? where + "\nВ другой базе есть этот же контакт с другими данными — разберите расхождение синхронизацией."
                    : where;
            }
        }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public string Comment
        {
            get => _comment;
            set => Set(ref _comment, value);
        }

        public string AnyDesk
        {
            get => _anyDesk;
            set => Set(ref _anyDesk, value);
        }

        public string Rudesktop
        {
            get => _rudesktop;
            set => Set(ref _rudesktop, value);
        }

        public string Assistant
        {
            get => _assistant;
            set => Set(ref _assistant, value);
        }

        public string Ammyy
        {
            get => _ammyy;
            set => Set(ref _ammyy, value);
        }

        public string Rdp
        {
            get => _rdp;
            set => Set(ref _rdp, value);
        }

        public string Password
        {
            get => _password;
            set
            {
                if (Set(ref _password, value))
                {
                    OnPropertyChanged(nameof(PasswordMasked));
                }
            }
        }

        public string RdpLogin
        {
            get => _rdpLogin;
            set => Set(ref _rdpLogin, value);
        }

        public string RdpPassword
        {
            get => _rdpPassword;
            set
            {
                if (Set(ref _rdpPassword, value))
                {
                    OnPropertyChanged(nameof(RdpPasswordMasked));
                }
            }
        }

        /// <summary>Группа контакта. null — «Без группы».</summary>
        public long? GroupId
        {
            get => _groupId;
            set => Set(ref _groupId, value);
        }

        /// <summary>Идентификаторы меток контакта (many-to-many).</summary>
        public HashSet<long> LabelIds { get; } = new HashSet<long>();

        public string PasswordMasked => string.IsNullOrEmpty(_password) ? string.Empty : new string((char)0x2022, 6);

        public string RdpPasswordMasked => string.IsNullOrEmpty(_rdpPassword) ? string.Empty : new string((char)0x2022, 6);

        public Contact Clone()
        {
            var copy = new Contact
            {
                Source = Source,
                CloudId = CloudId,
                Id = Id,
                Version = Version,
            };
            copy.CopyValuesFrom(this);
            return copy;
        }

        /// <summary>Копирует данные контакта (без идентичности: источника, Id, CloudId).</summary>
        public void CopyValuesFrom(Contact other)
        {
            Version = other.Version;
            GroupName = other.GroupName;
            LabelNames.Clear();
            LabelNames.UnionWith(other.LabelNames);
            Name = other.Name;
            Comment = other.Comment;
            AnyDesk = other.AnyDesk;
            Rudesktop = other.Rudesktop;
            Assistant = other.Assistant;
            Ammyy = other.Ammyy;
            Rdp = other.Rdp;
            Password = other.Password;
            RdpLogin = other.RdpLogin;
            RdpPassword = other.RdpPassword;
            GroupId = other.GroupId;
            LabelIds.Clear();
            foreach (var id in other.LabelIds)
            {
                LabelIds.Add(id);
            }
        }

        /// <summary>Значение поля контакта по ключу инструмента.</summary>
        public string GetToolId(string toolKey)
        {
            switch (toolKey)
            {
                case ToolKeys.AnyDesk: return AnyDesk;
                case ToolKeys.Rudesktop: return Rudesktop;
                case ToolKeys.Assistant: return Assistant;
                case ToolKeys.Ammyy: return Ammyy;
                case ToolKeys.Rdp: return Rdp;
                default: return string.Empty;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
