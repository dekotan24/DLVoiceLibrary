using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DLVoiceLibrary;

/// <summary>
/// WPFのタイトルバーはOSのライトテーマ(白)のまま描画されるため、DWMにダークモードを指示して
/// タイトルバーを暗くする。Windows 10 1809以降で有効。
/// </summary>
public static class DarkTitleBar
{
    // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 20H1以降)。1903-1909では未公開値の19が同じ意味を持つ。
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>ウィンドウのタイトルバーをダークモードにする。コンストラクタから呼ぶ(ハンドル生成後に適用される)。</summary>
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                var enable = 1;
                if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enable, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref enable, sizeof(int));
                }
            }
            catch
            {
                // 古いWindowsでdwmapiの属性が未対応でも、タイトルバーが白いままになるだけなので無視する
            }
        };
    }
}
