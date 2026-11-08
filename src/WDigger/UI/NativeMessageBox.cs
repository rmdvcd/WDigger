using System.Runtime.InteropServices;

namespace WDigger.UI;

/// <summary>
/// Minimal native message box (user32). Used only for fatal failures before
/// or outside the XAML framework, where a ContentDialog cannot be shown.
/// </summary>
public static class NativeMessageBox
{
    public enum Icon : uint
    {
        Warning = 0x00000030, // MB_ICONWARNING
        Error = 0x00000010   // MB_ICONERROR
    }

    private const uint MB_OK = 0x00000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string message, string title, Icon icon)
        => MessageBoxW(IntPtr.Zero, message, title, MB_OK | (uint)icon);
}
