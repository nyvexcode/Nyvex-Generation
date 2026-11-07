using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NyvexGeneration.Helpers;

public static class WindowHelper
{
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Ativa a barra de título escura (Windows 10 20H1+/11). Falhas são ignoradas.</summary>
    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var enabled = 1;
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception)
        {
            // Recurso apenas estético.
        }
    }
}
