using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RemoteAccessAddressBook.Models;
using RemoteAccessAddressBook.Services;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>Вопрос о расхождении: две версии контакта рядом, различия выделены.</summary>
    public partial class ConflictWindow : Window
    {
        public ConflictWindow(ConflictRequest request)
        {
            InitializeComponent();

            Title = request.Title ?? "Расхождение";
            MessageText.Text = request.Message;
            LeftButton.Content = request.LeftButton;
            RightButton.Content = request.RightButton;
            if (string.IsNullOrEmpty(request.BothButton))
            {
                BothButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                BothButton.Content = request.BothButton;
            }

            ApplyToAllCheckBox.Visibility = request.ShowApplyToAll ? Visibility.Visible : Visibility.Collapsed;

            AddRow(string.Empty, request.LeftTitle, request.RightTitle, header: true);
            var left = request.Left;
            var right = request.Right;
            AddRow("Имя", left.Name, right.Name);
            AddRow("Комментарий", left.Comment, right.Comment);
            AddRow("AnyDesk", left.AnyDesk, right.AnyDesk);
            AddRow("Rudesktop", left.Rudesktop, right.Rudesktop);
            AddRow("Ассистент", left.Assistant, right.Assistant);
            AddRow("AmmyyAdmin", left.Ammyy, right.Ammyy);
            AddRow("Пароль", left.Password, right.Password, secret: true);
            AddRow("RDP", left.Rdp, right.Rdp);
            AddRow("RDP Логин", left.RdpLogin, right.RdpLogin);
            AddRow("RDP Пароль", left.RdpPassword, right.RdpPassword, secret: true);
            AddRow("Группа", left.GroupName, right.GroupName, ignoreCase: true);
            AddRow("Метки", Labels(left), Labels(right), ignoreCase: true);
        }

        public ConflictDecision Decision { get; private set; } = new ConflictDecision { Choice = ConflictChoice.Cancel };

        private static string Labels(Contact contact) =>
            string.Join(", ", contact.LabelNames.OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase));

        private void AddRow(string field, string left, string right, bool header = false, bool secret = false, bool ignoreCase = false)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            var differs = !header && !string.Equals(left.Trim(), right.Trim(),
                ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.Ordinal);

            var row = CompareGrid.RowDefinitions.Count;
            CompareGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var background = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource(header ? "GridHeaderBrush" : differs ? "SelectionBrush" : "SurfaceBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushLight"),
                BorderThickness = new Thickness(0, row == 0 ? 0 : 1, 0, 0),
            };
            Grid.SetRow(background, row);
            Grid.SetColumnSpan(background, 3);
            CompareGrid.Children.Add(background);

            AddCell(field, row, 0, muted: !header, bold: header);
            AddCell(secret && !differs ? Mask(left) : left, row, 1, bold: header || differs);
            AddCell(secret && !differs ? Mask(right) : right, row, 2, bold: header || differs);
        }

        /// <summary>Совпадающие пароли не показываем; различающиеся видны — иначе не выбрать.</summary>
        private static string Mask(string value) => string.IsNullOrEmpty(value) ? string.Empty : new string('•', 6);

        private void AddCell(string text, int row, int column, bool muted = false, bool bold = false)
        {
            var block = new TextBlock
            {
                Text = text,
                Margin = new Thickness(8, 5, 8, 5),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedTextBrush" : "TextBrush");
            Grid.SetRow(block, row);
            Grid.SetColumn(block, column);
            CompareGrid.Children.Add(block);
        }

        private void Choose(ConflictChoice choice)
        {
            Decision = new ConflictDecision { Choice = choice, ApplyToAll = ApplyToAllCheckBox.IsChecked == true };
            DialogResult = true;
        }

        private void Left_Click(object sender, RoutedEventArgs e) => Choose(ConflictChoice.Left);

        private void Right_Click(object sender, RoutedEventArgs e) => Choose(ConflictChoice.Right);

        private void Both_Click(object sender, RoutedEventArgs e) => Choose(ConflictChoice.Both);
    }
}
