using System;
using System.Collections.Generic;
using System.Linq;

namespace MMTGui;

/// <summary>
/// 本地歌词导入时的「模式」。
///
/// 刻意与在线歌词的 <see cref="LyricsDisplayMode"/> 分开：
/// 后者的取值顺序直接绑定在线界面的下拉框索引，改动会破坏其语义。
/// </summary>
internal enum LocalLyricsMode
{
    /// <summary>原文+译文：同一时间戳内，第一行作原文、其余作译文，按格式组合。</summary>
    OriginalAndTranslation = 0,

    /// <summary>独立：不做原文/译文区分，每个时间戳一块文本，逐行原样输出。</summary>
    Independent = 1
}

/// <summary>
/// 本地歌词文件（LRC / SRT / ASS）导入时的双语重组支持。
///
/// 与在线歌词（<see cref="OnlineLyricsFormatter"/>）不同的是：
/// 在线接口直接给出**两份**歌词（原文、译文），而本地文件通常是一份混排的。
/// 因此这里采用「按时间戳分组」的判定方式：
///
///   同一时间戳下的第一行 = 原文，其余行 = 译文。
///
/// 这对 LRC 的 <c>[00:12.00]原文</c> / <c>[00:12.00]译文</c>、
/// 以及 SRT/ASS 中同一时间轴的多行文本都成立。
///
/// 「模式」决定取哪些内容，「格式」决定双语如何组合 —— 语义与在线歌词保持一致。
/// </summary>
internal static class LocalLyricsSupport
{
    /// <summary>
    /// 一条歌词在各时间戳上的原始文本集合。
    /// </summary>
    internal sealed class TimedGroup
    {
        public TimedGroup(string timeTag, double timeSeconds, int order)
        {
            TimeTag = timeTag;
            TimeSeconds = timeSeconds;
            Order = order;
        }

        /// <summary>标准化后的时间标签（如 <c>00:12.00</c>）。</summary>
        public string TimeTag { get; }

        /// <summary>该时间戳对应的秒数。</summary>
        public double TimeSeconds { get; }

        /// <summary>首次出现顺序，用于同一秒内稳定排序。</summary>
        public int Order { get; }

        /// <summary>时间戳下的所有非空文本行，第一行为原文，其余为译文。</summary>
        public List<string> TextLines { get; } = new();

        /// <summary>原文（取第一行）。</summary>
        public string? Original => TextLines.Count > 0 ? TextLines[0] : null;

        /// <summary>译文（取第一行之后的全部内容）。</summary>
        public IReadOnlyList<string> Translations =>
            TextLines.Count > 1 ? TextLines.GetRange(1, TextLines.Count - 1) : Array.Empty<string>();
    }

    /// <summary>
    /// 按键为「时间戳（取整到毫秒）」把歌词行归并成组。
    ///
    /// 之所以按毫秒取整而不是直接按时间字符串，是为了让
    /// <c>[00:12.00]</c> 与 <c>[00:12.000]</c> 这类写法能正确配对。
    /// </summary>
    public static List<TimedGroup> GroupByTimestamp(IEnumerable<LrcLine> lines)
    {
        var groups = new Dictionary<long, TimedGroup>();
        var order = 0;

        foreach (var line in lines.Where(l => l.IsTimed && l.TimeSeconds.HasValue))
        {
            var seconds = line.TimeSeconds!.Value;
            var key = (long)Math.Round(Math.Max(0, seconds) * 1000.0);

            if (!groups.TryGetValue(key, out var group))
            {
                group = new TimedGroup(line.TimeTag ?? string.Empty, seconds, order++);
                groups[key] = group;
            }

            // 一个 LRC 行内部可能已经含有多行（SRT/ASS 的换行文本）。
            foreach (var piece in SplitText(line.Text))
            {
                if (!group.TextLines.Contains(piece, StringComparer.Ordinal))
                    group.TextLines.Add(piece);
            }
        }

        return groups.Values
            .OrderBy(g => g.TimeSeconds)
            .ThenBy(g => g.Order)
            .ToList();
    }

    /// <summary>
    /// 按模式与格式，把分组结果转换成待插入脚本的字幕块。
    /// </summary>
    /// <param name="groups">按时间戳归并后的歌词组。</param>
    /// <param name="mode">模式：原文+译文 / 独立。</param>
    /// <param name="combineMode">格式：合并 / 混合 / 独立。</param>
    public static List<LyricScriptBlock> BuildBlocks(
        IReadOnlyList<TimedGroup> groups,
        LocalLyricsMode mode,
        LyricsCombineMode combineMode)
    {
        var blocks = new List<LyricScriptBlock>();

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];

            // 结束时间取下一组的时间戳；最后一组默认延续 3 秒。
            var end = i + 1 < groups.Count
                ? groups[i + 1].TimeSeconds
                : group.TimeSeconds + 3.0;

            if (end <= group.TimeSeconds)
                end = group.TimeSeconds + 3.0;

            var textLines = BuildTextLines(group, mode, combineMode);
            if (textLines.Count == 0)
                continue;

            if (mode == LocalLyricsMode.OriginalAndTranslation
                && combineMode == LyricsCombineMode.Independent)
            {
                // 格式=独立：原文与译文各自成为独立字幕块。
                // 关键点：把 [start, end) 均分给各行，而不是让它们共用同一时间段
                // —— 否则会生成多条时间重叠的字幕块，在 MMD 里互相覆盖。
                var span = end - group.TimeSeconds;
                if (span <= 0)
                    span = 3.0;

                var slice = span / textLines.Count;

                for (int lineIndex = 0; lineIndex < textLines.Count; lineIndex++)
                {
                    var lineStart = group.TimeSeconds + slice * lineIndex;
                    var lineEnd = group.TimeSeconds + slice * (lineIndex + 1);

                    blocks.Add(new LyricScriptBlock
                    {
                        StartSeconds = lineStart,
                        EndSeconds = lineEnd,
                        TextLines = new List<string> { textLines[lineIndex] }
                    });
                }

                continue;
            }

            blocks.Add(new LyricScriptBlock
            {
                StartSeconds = group.TimeSeconds,
                EndSeconds = end,
                TextLines = textLines
            });
        }

        return blocks;
    }

    /// <summary>
    /// 生成组内需要输出的文本行。
    /// </summary>
    private static List<string> BuildTextLines(
        TimedGroup group,
        LocalLyricsMode mode,
        LyricsCombineMode combineMode)
    {
        // 模式=独立：不做原文/译文区分，按行原样输出。
        if (mode == LocalLyricsMode.Independent)
            return group.TextLines.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        var original = group.Original;
        var translations = group.Translations;

        // 没有译文时退化为仅原文，避免产生空行。
        if (translations.Count == 0)
            return string.IsNullOrWhiteSpace(original) ? new List<string>() : new List<string> { original! };

        return combineMode switch
        {
            LyricsCombineMode.Merged => BuildMergedLines(original, translations),
            _ => BuildMixedLines(original, translations)
        };
    }

    /// <summary>格式=合并：原文与译文同行合并。</summary>
    private static List<string> BuildMergedLines(string? original, IReadOnlyList<string> translations)
    {
        var result = new List<string>();
        var count = Math.Max(1, translations.Count);

        for (int i = 0; i < count; i++)
        {
            var translated = i < translations.Count ? translations[i] : null;
            var merged = JoinText(i == 0 ? original : null, translated);
            if (!string.IsNullOrWhiteSpace(merged))
                result.Add(merged);
        }

        return result;
    }

    /// <summary>格式=混合：原文一行，译文各自成行，同一个字幕块内换行显示。</summary>
    private static List<string> BuildMixedLines(string? original, IReadOnlyList<string> translations)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(original))
            result.Add(original!.Trim());

        result.AddRange(translations.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        return result;
    }

    /// <summary>按换行拆分为非空文本行。</summary>
    private static IEnumerable<string> SplitText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var piece in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = piece.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    /// <summary>用空格连接原文与译文，自动跳过空值。</summary>
    private static string JoinText(string? original, string? translated)
    {
        var parts = new[] { original?.Trim(), translated?.Trim() }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }
}
