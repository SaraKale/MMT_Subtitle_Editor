using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// MainWindow 对话框与通用提示分部类。
/// 把状态提示、确认弹窗、错误弹窗单独收拢，
/// 便于后续统一替换为更完整的消息服务。
/// </summary>
public partial class MainWindow
{
    #region 工具方法

    /// <summary>
    /// 更新底部状态栏文本。
    /// </summary>
    private void SetStatus(string message)
    {
        _lastStatusMessage = message;
        if (StatusText != null)
            StatusText.Text = LocalizationService.T(message);
    }

    /// <summary>
    /// 确保脚本文本以 <c>:end</c> 结束，并返回修正后的字符串。
    /// </summary>
    private string EnsureEndCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ":end";

        var trimmed = content.TrimEnd();
        if (!trimmed.EndsWith(":end", StringComparison.OrdinalIgnoreCase))
            return trimmed + "\n:end";

        return content;
    }

    /// <summary>
    /// 显示保存确认对话框。
    /// 返回 <c>true</c> 表示保存，<c>false</c> 表示取消关闭，<c>null</c> 表示继续但不保存。
    /// </summary>
    private async Task<bool?> ShowConfirmDialog(string message)
    {
        var result = false;

        var dialog = new SukiWindow
        {
            Title = "确认",  // 确认
            Width = 360,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "取消", Classes = { "Outlined" } },  // 取消
                            new Button { Content = "不保存", Classes = { "Outlined" } },  // 不保存
                            new Button { Content = "保存", Classes = { "Accent" } }  // 保存
                        }
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel &&
            panel.Children[1] is StackPanel btnPanel)
        {
            if (btnPanel.Children[0] is Button cancelBtn)
                cancelBtn.Click += (_, _) => { result = false; dialog.Close(); };
            if (btnPanel.Children[1] is Button noSaveBtn)
                noSaveBtn.Click += (_, _) => { dialog.Close(); };
            if (btnPanel.Children[2] is Button saveBtn)
                saveBtn.Click += (_, _) => { result = true; dialog.Close(); };
        }

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>
    /// 显示错误提示对话框。
    /// </summary>
    private async Task ShowErrorDialog(string message)
    {
        var dialog = new SukiWindow
        {
            Title = "错误",  // 错误
            Width = 360,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, FontSize = 13, Foreground = Brushes.Red, TextWrapping = TextWrapping.Wrap },
                    new Button
                    {
                        Content = "确定",  // 确定
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Classes = { "Outlined" }
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel &&
            panel.Children[1] is Button btn)
            btn.Click += (_, _) => dialog.Close();

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);
    }

    #endregion
}
