using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RemoteAccessAddressBook.Services
{
    /// <summary>
    /// Запись текста в буфер обмена напрямую через Win32 (OpenClipboard/SetClipboardData).
    /// WPF-шный Clipboard.SetDataObject идёт через OLE (OleSetClipboard), и на компьютерах,
    /// где запущены программы, следящие за буфером (AnyDesk, Rudesktop, «Ассистент» и т. п.),
    /// стабильно падает с CLIPBRD_E_CANT_OPEN, хотя буфер свободен.
    /// </summary>
    public static class ClipboardText
    {
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        public static bool TrySet(string text)
        {
            text ??= string.Empty;

            // Буфер может быть на мгновение занят другим процессом — повторяем.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (TrySetWin32(text))
                {
                    return true;
                }

                Thread.Sleep(50);
            }

            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TrySetWin32(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            try
            {
                if (!EmptyClipboard())
                {
                    return false;
                }

                var chars = (text + "\0").ToCharArray();
                var handle = GlobalAlloc(GmemMoveable, (UIntPtr)(chars.Length * sizeof(char)));
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                {
                    GlobalFree(handle);
                    return false;
                }

                Marshal.Copy(chars, 0, pointer, chars.Length);
                GlobalUnlock(handle);

                // После успешного SetClipboardData памятью владеет система.
                if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
                {
                    GlobalFree(handle);
                    return false;
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr newOwner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
