namespace RemoteAccessAddressBook.ViewModels
{
    /// <summary>Строка настроек одного инструмента в окне настроек.</summary>
    public class ToolSettingRow : ViewModelBase
    {
        private string _exePath = string.Empty;
        private string _arguments = string.Empty;
        private bool _passwordToStdin;
        private bool _useCmdKey;
        private bool _copyIdToClipboard;

        public ToolSettingRow(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public string ExePath
        {
            get => _exePath;
            set => Set(ref _exePath, value);
        }

        public string Arguments
        {
            get => _arguments;
            set => Set(ref _arguments, value);
        }

        public bool PasswordToStdin
        {
            get => _passwordToStdin;
            set => Set(ref _passwordToStdin, value);
        }

        public bool UseCmdKey
        {
            get => _useCmdKey;
            set => Set(ref _useCmdKey, value);
        }

        public bool CopyIdToClipboard
        {
            get => _copyIdToClipboard;
            set => Set(ref _copyIdToClipboard, value);
        }
    }
}
