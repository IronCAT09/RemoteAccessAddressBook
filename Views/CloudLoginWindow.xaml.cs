using System;
using System.Windows;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Вход на сервер облачной базы. Ошибки показываются в самом окне, чтобы поправить и повторить.</summary>
    public partial class CloudLoginWindow : Window
    {
        public CloudLoginWindow(string serverUrl, string login)
        {
            InitializeComponent();
            ServerBox.Text = serverUrl ?? string.Empty;
            LoginBox.Text = login ?? string.Empty;
            Loaded += (_, _) =>
            {
                if (string.IsNullOrEmpty(ServerBox.Text))
                {
                    ServerBox.Focus();
                }
                else if (string.IsNullOrEmpty(LoginBox.Text))
                {
                    LoginBox.Focus();
                }
                else
                {
                    PasswordInput.Focus();
                }
            };
        }

        public CloudLoginOutcome Outcome { get; private set; }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var url = CloudClient.NormalizeUrl(ServerBox.Text);
                if (string.IsNullOrWhiteSpace(LoginBox.Text) || PasswordInput.Password.Length == 0)
                {
                    throw new CloudException("Укажите логин и пароль.");
                }

                using var client = new CloudClient(url);
                var result = await client.LoginAsync(LoginBox.Text.Trim(), PasswordInput.Password);
                Outcome = new CloudLoginOutcome { ServerUrl = url, Login = result };
                DialogResult = true;
            }
            catch (Exception ex) when (ex is CloudException || ex is CloudAuthException)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                PasswordInput.Focus();
                PasswordInput.SelectAll();
            }
            finally
            {
                LoginButton.IsEnabled = true;
                Cursor = null;
            }
        }
    }
}
