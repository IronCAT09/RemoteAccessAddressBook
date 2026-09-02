using System.Windows;

namespace RemouteAddressBook.Views
{
    /// <summary>Окно ввода одной строки (название группы или метки).</summary>
    public partial class TextPromptWindow : Window
    {
        public TextPromptWindow(string title, string caption, string initialValue)
        {
            InitializeComponent();

            Title = title;
            CaptionText.Text = caption;
            ValueBox.Text = initialValue ?? string.Empty;
            ValueBox.SelectAll();
            ValueBox.Focus();
        }

        /// <summary>Введённое значение.</summary>
        public string Value { get; private set; }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Value = ValueBox.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
