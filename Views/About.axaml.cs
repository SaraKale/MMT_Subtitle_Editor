using Avalonia.Interactivity;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// “关于”窗口的代码后置。
/// 该窗口只负责展示静态信息与关闭操作，因此逻辑保持极简。
/// </summary>
public partial class About : SukiWindow
{
    /// <summary>
    /// 初始化窗口组件。
    /// </summary>
    public About()
    {
        InitializeComponent();
        LocalizationService.RegisterWindow(this);
    }

    /// <summary>
    /// 关闭“关于”窗口。
    /// </summary>
    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
