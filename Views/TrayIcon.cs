using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace RemoteAccessAddressBook.Views
{
    /// <summary>
    /// Значок в области уведомлений для режима «Сворачивать в трей».
    /// Виден, только пока окно спрятано.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;

        public TrayIcon(string text, Action restore, Action exit)
        {
            _icon = new Forms.NotifyIcon
            {
                Text = text.Length > 63 ? text.Substring(0, 63) : text,
                Icon = LoadIcon(),
                Visible = false,
            };

            var menu = new Forms.ContextMenuStrip();
            var open = menu.Items.Add("Открыть", null, (s, e) => restore());
            open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => exit());
            _icon.ContextMenuStrip = menu;

            _icon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    restore();
                }
            };
        }

        public bool Visible
        {
            get => _icon.Visible;
            set => _icon.Visible = value;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Icon?.Dispose();
            _icon.Dispose();
        }

        private static System.Drawing.Icon LoadIcon()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource == null)
            {
                return System.Drawing.SystemIcons.Application;
            }

            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
    }
}
