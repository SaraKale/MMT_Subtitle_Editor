using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MMTGui;

/// <summary>
/// MainWindow 编辑操作分部类。
/// 集中处理撤销、重做、剪切、复制、粘贴等编辑行为，
/// 让主文件中的初始化与渲染逻辑更容易阅读。
/// </summary>
public partial class MainWindow
{
    #region 编辑操作

    /// <summary>
    /// 保存当前脚本文本到撤销历史栈。
    /// 仅在非撤销/重做过程中记录，避免把回放动作再次写入历史。
    /// </summary>
    private void SaveToHistory()
    {
        if (_isUndoRedoing) return;

        if (ScriptEditor != null)
        {
            var currentText = ScriptEditor.Text;
            _undoStack.Push(currentText);

            if (_undoStack.Count > MaxHistorySize)
            {
                var temp = new Stack<string>();
                for (int i = 0; i < MaxHistorySize - 1; i++)
                {
                    temp.Push(_undoStack.Pop());
                }
                _undoStack = new Stack<string>(temp.Reverse());
            }

            _redoStack.Clear();
        }
    }

    /// <summary>
    /// 执行撤销操作，恢复到上一份脚本文本快照。
    /// </summary>
    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0 || ScriptEditor == null)
        {
            SetStatus("没有可撤销的操作");  // 没有可撤销的操作
            return;
        }

        _isUndoRedoing = true;
        try
        {
            var currentText = ScriptEditor.Text;
            _redoStack.Push(currentText);

            var previousText = _undoStack.Pop();
            ScriptEditor.Text = previousText;

            ParseScriptToSubtitles();
            RefreshSubtitleList();
            SetStatus("已撤销");  // 已撤销
        }
        finally
        {
            _isUndoRedoing = false;
        }
    }

    /// <summary>
    /// 执行重做操作，恢复撤销后保存到重做栈中的文本快照。
    /// </summary>
    private void OnRedo(object? sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0 || ScriptEditor == null)
        {
            SetStatus("没有可重做的操作");  // 没有可重做的操作
            return;
        }

        _isUndoRedoing = true;
        try
        {
            var currentText = ScriptEditor.Text;
            _undoStack.Push(currentText);

            var nextText = _redoStack.Pop();
            ScriptEditor.Text = nextText;

            ParseScriptToSubtitles();
            RefreshSubtitleList();
            SetStatus("已重做");  // 已重做
        }
        finally
        {
            _isUndoRedoing = false;
        }
    }

    /// <summary>
    /// 剪切当前选中的字幕条目。
    /// 当脚本编辑器获得焦点时，优先使用编辑器本身的剪切行为。
    /// </summary>
    private async void OnCut(object? sender, RoutedEventArgs e)
    {
        if (ScriptEditor?.IsEditorFocused == true)
        {
            await ScriptEditor.CutAsync();
            return;
        }

        if (SubtitleList == null || SubtitleList.SelectedItem is not SubtitleEntry entry)
        {
            SetStatus("请先选择要剪切的字幕条目");  // 请先选择要剪切的字幕条目
            return;
        }

        _clipboardSubtitle = CloneSubtitleEntry(entry);
        _subtitles.Remove(entry);
        SyncSubtitlesToScript();
        SetStatus("已剪切字幕条目");  // 已剪切字幕条目
    }

    /// <summary>
    /// 复制当前选中的字幕条目。
    /// 当焦点位于脚本编辑器时，改为执行文本复制。
    /// </summary>
    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (ScriptEditor?.IsEditorFocused == true)
        {
            await ScriptEditor.CopyAsync();
            return;
        }

        if (SubtitleList == null || SubtitleList.SelectedItem is not SubtitleEntry entry)
        {
            SetStatus("请先选择要复制的字幕条目");  // 请先选择要复制的字幕条目
            return;
        }

        _clipboardSubtitle = CloneSubtitleEntry(entry);
        SetStatus("已复制字幕条目");  // 已复制字幕条目
    }

    /// <summary>
    /// 将剪贴板中的字幕条目粘贴到当前选中条目之后。
    /// 如果脚本编辑器正处于输入焦点，则直接转交给编辑器进行文本粘贴。
    /// </summary>
    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        if (ScriptEditor?.IsEditorFocused == true)
        {
            await ScriptEditor.PasteAsync();
            return;
        }

        if (_clipboardSubtitle == null)
        {
            SetStatus("剪贴板为空");  // 剪贴板为空
            return;
        }

        var newEntry = CloneSubtitleEntry(_clipboardSubtitle);
        var insertIndex = _subtitles.Count;

        if (SubtitleList != null && SubtitleList.SelectedItem is SubtitleEntry selected)
        {
            insertIndex = _subtitles.IndexOf(selected) + 1;
        }

        _subtitles.Insert(insertIndex, newEntry);
        SyncSubtitlesToScript();
        if (SubtitleList != null)
            SubtitleList.SelectedIndex = insertIndex;
        SetStatus("已粘贴字幕条目");  // 已粘贴字幕条目
    }

    /// <summary>
    /// 深拷贝字幕条目，避免多个列表项共享同一对象引用。
    /// </summary>
    private static SubtitleEntry CloneSubtitleEntry(SubtitleEntry source)
    {
        return new SubtitleEntry
        {
            Text = source.Text,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            FontName = source.FontName,
            FontSize = source.FontSize,
            FontColor = source.FontColor,
            EdgeColor = source.EdgeColor,
            ShadowColor = source.ShadowColor,
            BackColor = source.BackColor,
            EdgeWidth = source.EdgeWidth,
            EdgeRound = source.EdgeRound,
            Edge2Width = source.Edge2Width,
            Edge2Color = source.Edge2Color,
            Edge3Width = source.Edge3Width,
            Edge3Color = source.Edge3Color,
            Edge4Width = source.Edge4Width,
            Edge4Color = source.Edge4Color,
            GlowRadius = source.GlowRadius,
            GlowColor = source.GlowColor,
            GlowAlpha = source.GlowAlpha,
            EdgeGradientEnabled = source.EdgeGradientEnabled,
            EdgeGradientTop = source.EdgeGradientTop,
            EdgeGradientBottom = source.EdgeGradientBottom,
            TextGradientEnabled = source.TextGradientEnabled,
            TextGradientTop = source.TextGradientTop,
            TextGradientBottom = source.TextGradientBottom,
            TextGradientDirection = source.TextGradientDirection,
            ShadowDistance = source.ShadowDistance,
            ShadowBlur = source.ShadowBlur,
            Bold = source.Bold,
            Italic = source.Italic,
            Underline = source.Underline,
            Strikeout = source.Strikeout,
            Tate = source.Tate,
            HAlign = source.HAlign,
            VAlign = source.VAlign,
            PosX = source.PosX,
            PosY = source.PosY,
            PosZ = source.PosZ,
            Alpha = source.Alpha,
            Billboard = source.Billboard,
            Fade = source.Fade,
            MarginX = source.MarginX,
            MarginY = source.MarginY,
            ScreenWidth = source.ScreenWidth,
            ScreenHeight = source.ScreenHeight,
            ScaleX = source.ScaleX,
            ScaleY = source.ScaleY,
            Rotation = source.Rotation,
            Spacing = source.Spacing,
            AnimationType = source.AnimationType,
            AnimationDuration = source.AnimationDuration,
            AnimationDirection = source.AnimationDirection,
            AnimationBlindsCount = source.AnimationBlindsCount,
            AnimationBlurStrength = source.AnimationBlurStrength,
        };
    }

    #endregion
}
