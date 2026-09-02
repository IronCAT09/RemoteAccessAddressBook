using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemouteAddressBook.Models
{
    /// <summary>Контакт адресной книги.</summary>
    public class Contact : INotifyPropertyChanged
    {
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

        public long Id
        {
            get => _id;
            set => Set(ref _id, value);
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
                Id = Id,
                Name = Name,
                Comment = Comment,
                AnyDesk = AnyDesk,
                Rudesktop = Rudesktop,
                Assistant = Assistant,
                Ammyy = Ammyy,
                Rdp = Rdp,
                Password = Password,
                RdpLogin = RdpLogin,
                RdpPassword = RdpPassword,
                GroupId = GroupId,
            };
            foreach (var id in LabelIds)
            {
                copy.LabelIds.Add(id);
            }

            return copy;
        }

        /// <summary>Копирует все поля (кроме Id) из другого контакта.</summary>
        public void CopyValuesFrom(Contact other)
        {
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
