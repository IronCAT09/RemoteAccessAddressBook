using System.Windows;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Ввод парольной фразы (скрыто). Для новой фразы — дважды и не короче 10 символов.</summary>
    public partial class PassphraseWindow : Window
    {
        private const int MinNewLength = 10;

        private readonly bool _confirm;

        public PassphraseWindow(string title, string message, bool confirm)
        {
            InitializeComponent();

            Title = title;
            MessageText.Text = message;
            _confirm = confirm;
            ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
            Passphrase_Changed(this, null);
            Loaded += (_, _) => FirstBox.Focus();
        }

        /// <summary>Введённая фраза (после ОК).</summary>
        public string Passphrase { get; private set; }

        private void Passphrase_Changed(object sender, RoutedEventArgs e)
        {
            if (!_confirm)
            {
                OkButton.IsEnabled = FirstBox.Password.Length > 0;
                HintText.Text = string.Empty;
                return;
            }

            if (FirstBox.Password.Length < MinNewLength)
            {
                HintText.Text = "Не короче " + MinNewLength + " символов. Удобно взять 3–4 случайных слова.";
                OkButton.IsEnabled = false;
            }
            else if (FirstBox.Password != SecondBox.Password)
            {
                HintText.Text = "Фразы не совпадают.";
                OkButton.IsEnabled = false;
            }
            else
            {
                HintText.Text = string.Empty;
                OkButton.IsEnabled = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Passphrase = FirstBox.Password;
            DialogResult = true;
        }
    }
}
