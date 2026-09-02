namespace RemouteAddressBook.Models
{
    /// <summary>Ключи поддерживаемых инструментов удалённого доступа.</summary>
    public static class ToolKeys
    {
        public const string AnyDesk = "anydesk";
        public const string Rudesktop = "rudesktop";
        public const string Assistant = "assistant";
        public const string Ammyy = "ammyy";
        public const string Rdp = "rdp";

        public static readonly string[] All = { AnyDesk, Rudesktop, Assistant, Ammyy, Rdp };

        public static string DisplayName(string key)
        {
            switch (key)
            {
                case AnyDesk: return "AnyDesk";
                case Rudesktop: return "Rudesktop";
                case Assistant: return "Ассистент";
                case Ammyy: return "AmmyyAdmin";
                case Rdp: return "RDP";
                default: return key;
            }
        }
    }
}
