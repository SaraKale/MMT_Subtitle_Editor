using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// MainWindow 字幕列表操作分部类。
/// 负责字幕列表的增删改查、脚本块同步，以及由字幕条目驱动的预览刷新。
/// </summary>
public partial class MainWindow
{
    #region 字幕列表操作

    private void OnAddSubtitle(object? sender, RoutedEventArgs e)
    {
        var newSub = new SubtitleEntry
        {
            Index = _subtitles.Count + 1,
            Text = LocalizationService.T("新字幕"), // 新字幕
            StartTime = 0,
            EndTime = 1.0,
            Preset = "def"
        };
        _subtitles.Add(newSub);
        RefreshSubtitleList();
        UpdateStatusBar();
        SetStatus("已添加字幕");  // 已添加字幕

        if (SubtitleList != null)
            SubtitleList.SelectedIndex = _subtitles.Count - 1;

        InsertSubtitleToScript(newSub);
    }

    // 插入字幕到脚本
    private void InsertSubtitleToScript(SubtitleEntry sub)
    {
        if (ScriptEditor == null) return;

        var timeLine = $"@{sub.StartTime:0.##},{sub.EndTime:0.##}";
        var lines = new[] { timeLine, sub.Text };

        ScriptEditor.InsertLineBeforeEnd(string.Join("\n", lines));
    }

    // 解析脚本中的字幕块，返回每个块的起止行索引
    private static List<ScriptSubtitleBlock> ParseScriptSubtitleBlocks(IReadOnlyList<string> lines)
    {
        var blocks = new List<ScriptSubtitleBlock>();

        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("@", StringComparison.Ordinal) || trimmed.StartsWith("@@", StringComparison.Ordinal))
                continue;

            var j = i + 1;
            while (j < lines.Count)
            {
                var nextTrimmed = lines[j].Trim();
                if (string.IsNullOrEmpty(nextTrimmed) ||
                    nextTrimmed.StartsWith(":", StringComparison.Ordinal) ||
                    (nextTrimmed.StartsWith("@", StringComparison.Ordinal) && !nextTrimmed.StartsWith("@@", StringComparison.Ordinal)))
                {
                    break;
                }

                j++;
            }

            blocks.Add(new ScriptSubtitleBlock(i, j));
            i = Math.Max(i, j - 1);
        }

        return blocks;
    }

    // 根据字幕条目构建脚本中的字幕块文本行
    private static List<string> BuildSubtitleBlockLines(SubtitleEntry sub)
    {
        var lines = new List<string>
        {
            $"@{sub.StartTime:0.##},{sub.EndTime:0.##}"
        };

        var textLines = (sub.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        lines.AddRange(textLines);
        return lines;
    }

    // 更新脚本中的字幕块
    private bool TryUpdateSubtitleBlockInScript(int subtitleIndex, SubtitleEntry sub)
    {
        if (ScriptEditor == null)
            return false;

        var lines = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .ToList();

        var blocks = ParseScriptSubtitleBlocks(lines);
        if (subtitleIndex < 0 || subtitleIndex >= blocks.Count)
            return false;

        var block = blocks[subtitleIndex];
        var replacement = BuildSubtitleBlockLines(sub);

        lines.RemoveRange(block.TimeLineIndex, block.TextEndLineIndexExclusive - block.TimeLineIndex);
        lines.InsertRange(block.TimeLineIndex, replacement);

        ScriptEditor.Text = string.Join(Environment.NewLine, lines);
        return true;
    }

    // 插入字幕到脚本中的指定位置
    private bool TryInsertSubtitleBlockAfterInScript(int subtitleIndex, SubtitleEntry sub)
    {
        if (ScriptEditor == null)
            return false;

        var lines = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .ToList();

        var blocks = ParseScriptSubtitleBlocks(lines);
        if (subtitleIndex < 0 || subtitleIndex >= blocks.Count)
            return false;

        var block = blocks[subtitleIndex];
        var insertIndex = block.TextEndLineIndexExclusive;
        var insertion = BuildSubtitleBlockLines(sub);

        if (insertIndex < lines.Count && !string.IsNullOrWhiteSpace(lines[insertIndex]))
            insertion.Add(string.Empty);

        lines.InsertRange(insertIndex, insertion);
        ScriptEditor.Text = string.Join(Environment.NewLine, lines);
        return true;
    }

    // 删除脚本中的字幕块
    private bool TryDeleteSubtitleBlockFromScript(int subtitleIndex)
    {
        if (ScriptEditor == null)
            return false;

        var lines = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .ToList();

        var blocks = ParseScriptSubtitleBlocks(lines);
        if (subtitleIndex < 0 || subtitleIndex >= blocks.Count)
            return false;

        var block = blocks[subtitleIndex];
        var removeCount = block.TextEndLineIndexExclusive - block.TimeLineIndex;

        if (block.TextEndLineIndexExclusive < lines.Count && string.IsNullOrWhiteSpace(lines[block.TextEndLineIndexExclusive]))
            removeCount++;

        lines.RemoveRange(block.TimeLineIndex, Math.Min(removeCount, lines.Count - block.TimeLineIndex));
        ScriptEditor.Text = string.Join(Environment.NewLine, lines);
        return true;
    }

    // 交换脚本中的字幕块位置
    private bool TrySwapSubtitleBlocksInScript(int firstIndex, int secondIndex)
    {
        if (ScriptEditor == null)
            return false;

        if (firstIndex == secondIndex ||
            firstIndex < 0 || secondIndex < 0 ||
            firstIndex >= _subtitles.Count || secondIndex >= _subtitles.Count)
        {
            return false;
        }

        var lines = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .ToList();

        var blocks = ParseScriptSubtitleBlocks(lines);
        if (firstIndex >= blocks.Count || secondIndex >= blocks.Count)
            return false;

        var replacementMap = new Dictionary<int, List<string>>
        {
            [firstIndex] = BuildSubtitleBlockLines(_subtitles[secondIndex]),
            [secondIndex] = BuildSubtitleBlockLines(_subtitles[firstIndex])
        };

        foreach (var blockIndex in replacementMap.Keys.OrderByDescending(index => blocks[index].TimeLineIndex))
        {
            var block = blocks[blockIndex];
            lines.RemoveRange(block.TimeLineIndex, block.TextEndLineIndexExclusive - block.TimeLineIndex);
            lines.InsertRange(block.TimeLineIndex, replacementMap[blockIndex]);
        }

        ScriptEditor.Text = string.Join(Environment.NewLine, lines);
        return true;
    }

    // 右键菜单 - 删除字幕
    private void OnDeleteSubtitle(object? sender, RoutedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex < 0) return;
        if (SubtitleList.SelectedIndex < _subtitles.Count)
        {
            var selectedIndex = SubtitleList.SelectedIndex;
            if (TryDeleteSubtitleBlockFromScript(selectedIndex))
            {
                SetStatus("已删除字幕");  // 已删除字幕
                return;
            }

            _subtitles.RemoveAt(selectedIndex);
            ReindexSubtitles();
            RefreshSubtitleList();
            SyncSubtitlesToScript();
            UpdateStatusBar();
            SetStatus("已删除字幕");  // 已删除字幕
        }
    }

    // 右键菜单 - 上移字幕
    private void OnMoveSubtitleUp(object? sender, RoutedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex <= 0) return;

        var idx = SubtitleList.SelectedIndex;
        if (TrySwapSubtitleBlocksInScript(idx, idx - 1))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SubtitleList != null)
                    SubtitleList.SelectedIndex = Math.Max(0, idx - 1);
            }, DispatcherPriority.Loaded);
            SetStatus("已上移字幕");  // 已上移字幕
            return;
        }

        (_subtitles[idx], _subtitles[idx - 1]) = (_subtitles[idx - 1], _subtitles[idx]);
        ReindexSubtitles();
        RefreshSubtitleList();
        SyncSubtitlesToScript();
        SubtitleList.SelectedIndex = idx - 1;
    }

    // 右键菜单 - 下移字幕
    private void OnMoveSubtitleDown(object? sender, RoutedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex >= _subtitles.Count - 1) return;

        var idx = SubtitleList.SelectedIndex;
        if (TrySwapSubtitleBlocksInScript(idx, idx + 1))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SubtitleList != null)
                    SubtitleList.SelectedIndex = Math.Min(_subtitles.Count - 1, idx + 1);
            }, DispatcherPriority.Loaded);
            SetStatus("已下移字幕");  // 已下移字幕
            return;
        }

        (_subtitles[idx], _subtitles[idx + 1]) = (_subtitles[idx + 1], _subtitles[idx]);
        ReindexSubtitles();
        RefreshSubtitleList();
        SyncSubtitlesToScript();
        SubtitleList.SelectedIndex = idx + 1;
    }

    // 右键菜单 - 修改字幕
    private void OnSubtitleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex < 0) return;

        if (SubtitleList.SelectedIndex < _subtitles.Count)
        {
            var sub = _subtitles[SubtitleList.SelectedIndex];
            UpdatePreviewFromSubtitle(sub);
        }
    }

    /// <summary>
    /// 右键菜单 - 修改字幕
    /// </summary>
    private async void OnEditSubtitle(object? sender, RoutedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex < 0) return;
        if (SubtitleList.SelectedIndex >= _subtitles.Count) return;

        var sub = _subtitles[SubtitleList.SelectedIndex];

        var txtText = new TextBox { Text = sub.Text, Width = 300 };
        var txtStart = new TextBox { Text = sub.StartTime.ToString("F2"), Width = 100 };
        var txtEnd = new TextBox { Text = sub.EndTime.ToString("F2"), Width = 100 };

        var dialog = new SukiWindow
        {
            Title = LocalizationService.T("修改字幕"),
            Width = 360,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanFullScreen = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = LocalizationService.T("字幕文本") + ":", FontSize = 12 },
                    txtText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new StackPanel { Children = { new TextBlock { Text = LocalizationService.T("开始(秒)") + ":", FontSize = 12 }, txtStart } },
                            new StackPanel { Children = { new TextBlock { Text = LocalizationService.T("结束(秒)") + ":", FontSize = 12 }, txtEnd } }
                        }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = LocalizationService.T("取消"), Classes = { "Outlined" } },
                            new Button { Content = LocalizationService.T("确定"), Classes = { "Accent" } }
                        }
                    }
                }
            }
        };

        bool confirmed = false;
        if (dialog.Content is StackPanel panel &&
            panel.Children[3] is StackPanel btnPanel)
        {
            if (btnPanel.Children[0] is Button cancelBtn)
                cancelBtn.Click += (_, _) => dialog.Close();
            if (btnPanel.Children[1] is Button okBtn)
                okBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        }

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);

        if (confirmed)
        {
            var updated = CloneSubtitleEntry(sub);
            updated.Text = txtText.Text ?? "";
            if (double.TryParse(txtStart.Text, out var st)) updated.StartTime = st;
            if (double.TryParse(txtEnd.Text, out var et)) updated.EndTime = et;

            if (TryUpdateSubtitleBlockInScript(SubtitleList.SelectedIndex, updated))
            {
                SetStatus("字幕已修改");  // 字幕已修改
                return;
            }

            sub.Text = updated.Text;
            sub.StartTime = updated.StartTime;
            sub.EndTime = updated.EndTime;
            RefreshSubtitleList();
            SyncSubtitlesToScript();
            UpdateStatusBar();
            SetStatus("字幕已修改");  // 字幕已修改
        }
    }

    /// <summary>
    /// 右键菜单 - 复制字幕
    /// </summary>
    private void OnCopySubtitle(object? sender, RoutedEventArgs e)
    {
        if (SubtitleList == null || SubtitleList.SelectedIndex < 0) return;
        if (SubtitleList.SelectedIndex < _subtitles.Count)
        {
            _clipboardSubtitle = new SubtitleEntry
            {
                Text = _subtitles[SubtitleList.SelectedIndex].Text,
                StartTime = _subtitles[SubtitleList.SelectedIndex].StartTime,
                EndTime = _subtitles[SubtitleList.SelectedIndex].EndTime,
                Preset = _subtitles[SubtitleList.SelectedIndex].Preset,
                FontName = _subtitles[SubtitleList.SelectedIndex].FontName,
                FontSize = _subtitles[SubtitleList.SelectedIndex].FontSize,
                FontColor = _subtitles[SubtitleList.SelectedIndex].FontColor,
                EdgeColor = _subtitles[SubtitleList.SelectedIndex].EdgeColor,
                ShadowColor = _subtitles[SubtitleList.SelectedIndex].ShadowColor,
                BackColor = _subtitles[SubtitleList.SelectedIndex].BackColor,
                EdgeWidth = _subtitles[SubtitleList.SelectedIndex].EdgeWidth,
                EdgeRound = _subtitles[SubtitleList.SelectedIndex].EdgeRound,
                ShadowDistance = _subtitles[SubtitleList.SelectedIndex].ShadowDistance,
                ShadowBlur = _subtitles[SubtitleList.SelectedIndex].ShadowBlur,
                Bold = _subtitles[SubtitleList.SelectedIndex].Bold,
                Italic = _subtitles[SubtitleList.SelectedIndex].Italic,
                Underline = _subtitles[SubtitleList.SelectedIndex].Underline,
                Strikeout = _subtitles[SubtitleList.SelectedIndex].Strikeout,
                Tate = _subtitles[SubtitleList.SelectedIndex].Tate,
                HAlign = _subtitles[SubtitleList.SelectedIndex].HAlign,
                VAlign = _subtitles[SubtitleList.SelectedIndex].VAlign,
                PosX = _subtitles[SubtitleList.SelectedIndex].PosX,
                PosY = _subtitles[SubtitleList.SelectedIndex].PosY,
                PosZ = _subtitles[SubtitleList.SelectedIndex].PosZ,
                Alpha = _subtitles[SubtitleList.SelectedIndex].Alpha,
                Billboard = _subtitles[SubtitleList.SelectedIndex].Billboard,
                Fade = _subtitles[SubtitleList.SelectedIndex].Fade,
                MarginX = _subtitles[SubtitleList.SelectedIndex].MarginX,
                MarginY = _subtitles[SubtitleList.SelectedIndex].MarginY,
                ScreenWidth = _subtitles[SubtitleList.SelectedIndex].ScreenWidth,
                ScreenHeight = _subtitles[SubtitleList.SelectedIndex].ScreenHeight,
                ScaleX = _subtitles[SubtitleList.SelectedIndex].ScaleX,
                ScaleY = _subtitles[SubtitleList.SelectedIndex].ScaleY,
                Rotation = _subtitles[SubtitleList.SelectedIndex].Rotation,
                Spacing = _subtitles[SubtitleList.SelectedIndex].Spacing,
                AnimationType = _subtitles[SubtitleList.SelectedIndex].AnimationType,
                AnimationDuration = _subtitles[SubtitleList.SelectedIndex].AnimationDuration,
                AnimationDirection = _subtitles[SubtitleList.SelectedIndex].AnimationDirection,
                AnimationBlindsCount = _subtitles[SubtitleList.SelectedIndex].AnimationBlindsCount,
                AnimationBlurStrength = _subtitles[SubtitleList.SelectedIndex].AnimationBlurStrength
            };
            SetStatus("已复制字幕");  // 已复制字幕
        }
    }

    /// <summary>
    /// 右键菜单 - 粘贴字幕
    /// </summary>
    private void OnPasteSubtitle(object? sender, RoutedEventArgs e)
    {
        if (_clipboardSubtitle == null)
        {
            SetStatus("剪贴板为空");  // 剪贴板为空
            return;
        }

        if (SubtitleList?.SelectedIndex >= 0 && TryInsertSubtitleBlockAfterInScript(SubtitleList.SelectedIndex, _clipboardSubtitle))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SubtitleList != null)
                    SubtitleList.SelectedIndex = Math.Min(_subtitles.Count - 1, Math.Max(0, SubtitleList.SelectedIndex + 1));
            }, DispatcherPriority.Loaded);
            SetStatus("已粘贴字幕");  // 已粘贴字幕
            return;
        }

        var newSub = new SubtitleEntry
        {
            Index = _subtitles.Count + 1,
            Text = _clipboardSubtitle.Text,
            StartTime = _clipboardSubtitle.StartTime,
            EndTime = _clipboardSubtitle.EndTime,
            Preset = _clipboardSubtitle.Preset,
            FontName = _clipboardSubtitle.FontName,
            FontSize = _clipboardSubtitle.FontSize,
            FontColor = _clipboardSubtitle.FontColor,
            EdgeColor = _clipboardSubtitle.EdgeColor,
            ShadowColor = _clipboardSubtitle.ShadowColor,
            BackColor = _clipboardSubtitle.BackColor,
            EdgeWidth = _clipboardSubtitle.EdgeWidth,
            EdgeRound = _clipboardSubtitle.EdgeRound,
            ShadowDistance = _clipboardSubtitle.ShadowDistance,
            ShadowBlur = _clipboardSubtitle.ShadowBlur,
            Bold = _clipboardSubtitle.Bold,
            Italic = _clipboardSubtitle.Italic,
            Underline = _clipboardSubtitle.Underline,
            Strikeout = _clipboardSubtitle.Strikeout,
            Tate = _clipboardSubtitle.Tate,
            HAlign = _clipboardSubtitle.HAlign,
            VAlign = _clipboardSubtitle.VAlign,
            PosX = _clipboardSubtitle.PosX,
            PosY = _clipboardSubtitle.PosY,
            PosZ = _clipboardSubtitle.PosZ,
            Alpha = _clipboardSubtitle.Alpha,
            Billboard = _clipboardSubtitle.Billboard,
            Fade = _clipboardSubtitle.Fade,
            MarginX = _clipboardSubtitle.MarginX,
            MarginY = _clipboardSubtitle.MarginY,
            ScreenWidth = _clipboardSubtitle.ScreenWidth,
            ScreenHeight = _clipboardSubtitle.ScreenHeight,
            ScaleX = _clipboardSubtitle.ScaleX,
            ScaleY = _clipboardSubtitle.ScaleY,
            Rotation = _clipboardSubtitle.Rotation,
            Spacing = _clipboardSubtitle.Spacing,
            AnimationType = _clipboardSubtitle.AnimationType,
            AnimationDuration = _clipboardSubtitle.AnimationDuration,
            AnimationDirection = _clipboardSubtitle.AnimationDirection,
            AnimationBlindsCount = _clipboardSubtitle.AnimationBlindsCount,
            AnimationBlurStrength = _clipboardSubtitle.AnimationBlurStrength
        };
        _subtitles.Add(newSub);
        ReindexSubtitles();
        RefreshSubtitleList();
        SyncSubtitlesToScript();
        UpdateStatusBar();
        SetStatus("已粘贴字幕");  // 已粘贴字幕
    }

    /// <summary>
    /// 将字幕列表同步回脚本编辑器
    /// </summary>
    private void SyncSubtitlesToScript()
    {
        if (ScriptEditor == null || _isSyncingFromProperty) return;
        _isSyncingFromProperty = true;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("// MMTScript - 由字幕列表同步生成");  // // MMTScript - 由字幕列表同步生成
            sb.AppendLine();

            string currentPreset = "def";
            foreach (var sub in _subtitles)
            {
                if (sub.Preset != currentPreset)
                {
                    if (sub.Preset == "def")
                        sb.AppendLine(":get def");
                    else
                        sb.AppendLine($":get {sub.Preset}");
                    currentPreset = sub.Preset;
                }

                sb.AppendLine($"@{sub.StartTime:0.##},{sub.EndTime:0.##}");
                sb.AppendLine(sub.Text);
                sb.AppendLine();
            }

            ScriptEditor.Text = sb.ToString().TrimEnd();
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // 更新字幕条目驱动的预览
    private void UpdatePreviewFromSubtitle(SubtitleEntry sub)
    {
        ApplyScreenSettingsToUi(sub.ScreenWidth, sub.ScreenHeight);
        UpdateAnimationControlsFromSubtitle(sub);
        RenderPreviewFromSubtitle(sub, _isPreviewing ? _previewStartTime : null);

        if (NudPosX != null) NudPosX.Value = (decimal)sub.PosX;
        if (NudPosY != null) NudPosY.Value = (decimal)sub.PosY;

        if (StatusTime != null)
            StatusTime.Text = $"{FormatTime(sub.StartTime)} - {FormatTime(sub.EndTime)} | {GetAnimationStatusText(sub)}";
    }

    // 渲染字幕预览
    private void RenderPreviewFromSubtitle(SubtitleEntry sub, double? currentTime = null)
    {
        if (PreviewCanvas == null) return;
        PreviewCanvas.Children.Clear();

        var screenWidth = sub.ScreenWidth > 0 ? sub.ScreenWidth : (double)(NudScreenWidth?.Value ?? 1280);
        var screenHeight = sub.ScreenHeight > 0 ? sub.ScreenHeight : (double)(NudScreenHeight?.Value ?? 720);
        var previewSize = UpdatePreviewFrame(screenWidth, screenHeight);
        var canvasW = previewSize.Width;
        var canvasH = previewSize.Height;
        if (canvasW <= 0 || canvasH <= 0) return;

        var scale = canvasH / screenHeight;

        var fontSize = sub.FontSize;
        var fsizepx = screenHeight * fontSize / 200.0;

        var fontFamily = sub.FontName;

        var typeface = new Typeface(fontFamily,
            sub.Italic ? FontStyle.Italic : FontStyle.Normal,
            sub.Bold ? FontWeight.Bold : FontWeight.Normal);

        var fontColor = sub.FontColor;
        var strokeColor = sub.EdgeColor;
        var shadowColor = sub.ShadowColor;
        var underline = sub.Underline;
        var strikeout = sub.Strikeout;
        var edgeRound = sub.EdgeRound;

        var edgeWidth = sub.EdgeWidth;
        var shadowDist = sub.ShadowDistance;
        var shadowBlur = sub.ShadowBlur;
        var marginX = sub.MarginX;
        var marginY = sub.MarginY;

        var hAlign = sub.HAlign;
        var vAlign = sub.VAlign;
        var isTate = sub.Tate;

        var scaleX = sub.ScaleX;
        var scaleY = sub.ScaleY;
        var rotation = sub.Rotation;
        var spacing = sub.Spacing;

        var scaledFontSize = fsizepx * scale;
        var animationTime = currentTime ?? GetPreviewSampleTime(sub);
        var animationState = GetPreviewAnimationState(sub, animationTime, scaledFontSize);
        var text = sub.Text;

        var measured = MeasureRenderedText(text, typeface, scaledFontSize, isTate, spacing);
        var transformedBounds = GetTransformedBounds(measured.Width, measured.Height, scaleX, scaleY, rotation);
        var textW = transformedBounds.Width;
        var textH = transformedBounds.Height;

        var origin = ComputeRenderOrigin(
            canvasW,
            canvasH,
            textW,
            textH,
            scaledFontSize,
            marginX,
            marginY,
            GetRenderSafetyPadding(scaledFontSize, edgeWidth, shadowDist, shadowBlur),
            isTate,
            hAlign,
            vAlign);
        var x = origin.X;
        var y = origin.Y;

        var pixelW = (int)canvasW;
        var pixelH = (int)canvasH;
        if (pixelW < 1) pixelW = 1;
        if (pixelH < 1) pixelH = 1;

        var renderBitmap = new RenderTargetBitmap(
            new PixelSize(pixelW, pixelH),
            new Vector(96, 96));

        using (var ctx = renderBitmap.CreateDrawingContext())
        {
            RenderTextToContext(
                ctx,
                new Point(x, y),
                text,
                typeface,
                scaledFontSize,
                fontColor,
                strokeColor,
                shadowColor,
                edgeWidth,
                shadowDist,
                shadowBlur,
                underline,
                strikeout,
                edgeRound,
                isTate,
                hAlign,
                vAlign,
                spacing,
                scaleX,
                scaleY,
                rotation,
                animationState);
        }

        var img = new Image
        {
            Source = renderBitmap,
            Width = canvasW,
            Height = canvasH,
        };

        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        PreviewCanvas.Children.Add(img);
    }

    // 更新预览框尺寸，并显示分辨率
    private Size UpdatePreviewFrame(double screenWidth, double screenHeight)
    {
        var size = GetPreviewFrameSize(screenWidth, screenHeight);

        if (PreviewBorder != null)
        {
            PreviewBorder.Width = size.Width;
            PreviewBorder.Height = size.Height;
        }

        if (PreviewResolution != null)
            PreviewResolution.Text = $"{screenWidth:0} × {screenHeight:0}";

        return size;
    }

    // 计算预览框尺寸
    private Size GetPreviewFrameSize(double screenWidth, double screenHeight)
    {
        var safeWidth = screenWidth > 0 ? screenWidth : 1280;
        var safeHeight = screenHeight > 0 ? screenHeight : 720;
        var fitScale = Math.Min(PreviewBaseWidth / safeWidth, PreviewBaseHeight / safeHeight);
        var scale = Math.Max(0.01, fitScale * _zoomLevel);

        return new Size(
            Math.Max(1.0, Math.Round(safeWidth * scale)),
            Math.Max(1.0, Math.Round(safeHeight * scale)));
    }

    // 重新索引字幕索引
    private void ReindexSubtitles()
    {
        for (int i = 0; i < _subtitles.Count; i++)
            _subtitles[i].Index = i + 1;
    }

    #endregion
}
