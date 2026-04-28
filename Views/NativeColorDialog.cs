using System;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace MMTGui;

/// <summary>
/// Windows 原生颜色选择对话框封装。
/// 在需要使用系统颜色选择器时，由各窗口代码后置统一调用。
/// </summary>
internal static class NativeColorDialog
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [DllImport("comdlg32.dll")]
    private static extern bool ChooseColor(ref CHOOSECOLOR cc);

    private const int CC_RGBINIT = 0x00000001;
    private const int CC_FULLOPEN = 0x00000002;

    [ThreadStatic]
    private static int[]? _customColors;

    /// <summary>
    /// 打开原生颜色选择对话框。
    /// </summary>
    public static Color? Show(IntPtr ownerHandle, Color currentColor)
    {
        _customColors ??= new int[16];

        var cc = new CHOOSECOLOR
        {
            lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner = ownerHandle,
            rgbResult = ColorToRgb(currentColor),
            lpCustColors = Marshal.UnsafeAddrOfPinnedArrayElement(_customColors, 0),
            Flags = CC_RGBINIT | CC_FULLOPEN
        };

        if (ChooseColor(ref cc))
        {
            return RgbToColor(cc.rgbResult);
        }

        return null;
    }

    /// <summary>
    /// 将 Avalonia 颜色转换为 Win32 RGB 整数。
    /// </summary>
    private static int ColorToRgb(Color color)
    {
        return (color.R) | (color.G << 8) | (color.B << 16);
    }

    /// <summary>
    /// 将 Win32 RGB 整数转换为 Avalonia 颜色。
    /// </summary>
    private static Color RgbToColor(int rgb)
    {
        return Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
    }
}
