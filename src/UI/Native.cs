using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace COTK.Next.UI;

/// <summary>Finitions Windows 11 via DWM : barre de titre sombre, coins arrondis natifs, liseré aux couleurs du
/// thème. Sans effet (et sans erreur) sur Windows 10.</summary>
internal static class Native
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void StyleWindow(Window window, Color border)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var dark = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var round = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
            var colorRef = border.R | (border.G << 8) | (border.B << 16);
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colorRef, sizeof(int));
        }
        catch
        {
            // Cosmétique uniquement.
        }
    }
}
