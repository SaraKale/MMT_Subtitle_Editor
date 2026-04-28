using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// 预览画布选项窗口代码后置。
/// 用于配置主窗口预览区域的底色与背景图片，并在确认后回传结果。
/// </summary>
public partial class Option : SukiWindow
{
    /// <summary>当前选中的预览画布颜色。</summary>
    public Color SelectedCanvasColor { get; private set; }
    /// <summary>当前选中的背景图片路径；为空表示不使用背景图。</summary>
    public string? SelectedBackgroundImagePath { get; private set; }
    /// <summary>用户是否点击了确认按钮。</summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// 使用默认参数构造选项窗口。
    /// </summary>
    public Option()
        : this(Color.FromRgb(22, 33, 62), null)
    {
    }

    /// <summary>
    /// 使用传入的初始配置构造选项窗口，便于打开时回显当前状态。
    /// </summary>
    public Option(Color initialCanvasColor, string? initialBackgroundImagePath)
    {
        InitializeComponent();
        SelectedCanvasColor = initialCanvasColor;
        SelectedBackgroundImagePath = string.IsNullOrWhiteSpace(initialBackgroundImagePath)
            ? null
            : initialBackgroundImagePath;
        UpdateUi();
        LocalizationService.RegisterWindow(this, UpdateUi);
    }

    /// <summary>
    /// 将当前状态同步到窗口控件。
    /// </summary>
    private void UpdateUi()
    {
        if (CanvasColorSwatch != null)
            CanvasColorSwatch.Background = new SolidColorBrush(SelectedCanvasColor);

        if (CanvasColorValue != null)
            CanvasColorValue.Text = $"ARGB {SelectedCanvasColor.A},{SelectedCanvasColor.R},{SelectedCanvasColor.G},{SelectedCanvasColor.B}";

        if (TxtBackgroundImagePath != null)
            TxtBackgroundImagePath.Text = SelectedBackgroundImagePath ?? string.Empty;
    }

    /// <summary>
    /// 打开颜色选择器，更新画布底色。
    /// </summary>
    private async void OnChooseCanvasColor(object? sender, RoutedEventArgs e)
    {
        var color = await PickColor(LocalizationService.T("选择画布颜色"), SelectedCanvasColor); // 选择画布颜色
        if (color.HasValue)
        {
            SelectedCanvasColor = color.Value;
            UpdateUi();
        }
    }

    /// <summary>
    /// 浏览并选择背景图片。
    /// </summary>
    private async void OnBrowseBackgroundImage(object? sender, RoutedEventArgs e)
    {
        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("选择画布背景图片"), // 选择画布背景图片
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                // 图片文件
                new FilePickerFileType(LocalizationService.T("图片文件")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif", "*.tiff", "*.svg","*.tga","*.dds","*.avif" } },
                // 所有文件
                new FilePickerFileType(LocalizationService.T("所有文件")) { Patterns = new[] { "*.*" } } 
            }
        });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            SelectedBackgroundImagePath = path;
            UpdateUi();
        }
    }

    /// <summary>
    /// 清空背景图片配置。
    /// </summary>
    private void OnClearBackgroundImage(object? sender, RoutedEventArgs e)
    {
        SelectedBackgroundImagePath = null;
        UpdateUi();
    }

    /// <summary>
    /// 取消并关闭窗口。
    /// </summary>
    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 确认设置并关闭窗口。
    /// </summary>
    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        Close();
    }

    /// <summary>
    /// 选择颜色。
    /// Windows 平台优先调用系统颜色对话框，其余平台退回内置拾色器。
    /// </summary>
    private async Task<Color?> PickColor(string title, Color currentColor)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = TryGetPlatformHandle();
            if (handle != null)
                return await Task.Run(() => NativeColorDialog.Show(handle.Handle, currentColor));
        }

        var picker = new ColorPicker
        {
            IsAlphaEnabled = true,
            IsColorPaletteVisible = true,
            Color = currentColor,
            Width = 280,
            Height = 350,
        };

        var dialog = new Window
        {
            Title = title,
            Width = 340,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                        picker,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Content = "取消", MinWidth = 60 },  // 取消
                                new Button { Content = "确定", MinWidth = 60 }  // 确定
                            }
                        }
                    }
                }
            }
        };

        Color? result = null;

        if (dialog.Content is Border border &&
            border.Child is StackPanel panel &&
            panel.Children[2] is StackPanel buttonPanel)
        {
            if (buttonPanel.Children[0] is Button cancelButton)
                cancelButton.Click += (_, _) => { result = null; dialog.Close(); };

            if (buttonPanel.Children[1] is Button okButton)
                okButton.Click += (_, _) => { result = picker.Color; dialog.Close(); };
        }

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);
        return result;
    }
}

internal class ColorPicker : Control
{
    public bool IsAlphaEnabled { get; set; }
    public bool IsColorPaletteVisible { get; set; }
    public Color Color { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}