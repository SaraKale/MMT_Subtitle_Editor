using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MMTGui;

/// <summary>
/// 歌词显示模式。
/// 决定界面与脚本导出时是显示原文、译文，还是双语同时显示。
/// </summary>
internal enum LyricsDisplayMode
{
    OriginalAndTranslation = 0,
    OriginalOnly = 1,
    TranslationOnly = 2
}

/// <summary>
/// 双语歌词组合模式。
/// 仅在“原文+译文”模式下生效。
/// </summary>
internal enum LyricsCombineMode
{
    Mixed = 0,
    Merged = 1,
    Independent = 2
}

/// <summary>
/// MMT 脚本插入时使用的时间码格式。
/// </summary>
internal enum LyricsTimeCodeFormat
{
    Standard = 0,
    Seconds = 1
}

/// <summary>
/// 单条 LRC 行模型。
/// 同时保留解析后的时间秒数、标准化时间标签与原始文本，便于后续做重组。
/// </summary>
internal sealed record LrcLine(double? TimeSeconds, string? TimeTag, string Text, string RawLine, int Order)
{
    public bool IsTimed => TimeSeconds.HasValue && !string.IsNullOrWhiteSpace(TimeTag);
}

/// <summary>
/// 导出到 MMT 脚本时使用的字幕块模型。
/// 一块对应一个时间区间及其多行文本。
/// </summary>
internal sealed class LyricScriptBlock
{
    public double StartSeconds { get; init; }
    public double EndSeconds { get; init; }
    public List<string> TextLines { get; init; } = new();
}

/// <summary>
/// 在线歌曲搜索与歌词加载后的统一歌曲模型。
/// 用来承接网易云、QQ 音乐两类来源的数据。
/// </summary>
public sealed class SearchSongItem
{
    public long SongId { get; set; }
    public string SongMid { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SongSource { get; set; } = string.Empty;
    public string SongName { get; set; } = string.Empty;
    public string Singer { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string SongDuration { get; set; } = string.Empty;
    public double SongDurationSeconds { get; set; }
    public string PublishDate { get; set; } = string.Empty;
    public string SongLink { get; set; } = string.Empty;
    public string OriginalLyrics { get; private set; } = string.Empty;
    public string TranslatedLyrics { get; private set; } = string.Empty;
    public bool LyricsLoaded { get; private set; }
    internal List<LrcLine> OriginalLyricLines { get; } = new();
    internal List<LrcLine> TranslatedLyricLines { get; } = new();

    /// <summary>
    /// 写入原文与译文歌词，并同步刷新解析后的 LRC 行集合。
    /// </summary>
    public void SetLyrics(string? originalLyrics, string? translatedLyrics)
    {
        OriginalLyrics = NormalizeLyrics(originalLyrics);
        TranslatedLyrics = NormalizeLyrics(translatedLyrics);

        OriginalLyricLines.Clear();
        OriginalLyricLines.AddRange(OnlineLyricsFormatter.ParseLrc(OriginalLyrics));

        TranslatedLyricLines.Clear();
        TranslatedLyricLines.AddRange(OnlineLyricsFormatter.ParseLrc(TranslatedLyrics));

        LyricsLoaded = !string.IsNullOrWhiteSpace(OriginalLyrics) || !string.IsNullOrWhiteSpace(TranslatedLyrics);
    }

    /// <summary>
    /// 获取搜索窗口中的默认歌词预览文本。
    /// </summary>
    public string GetPreviewLyrics()
    {
        return OnlineLyricsFormatter.FormatLyrics(this, LyricsDisplayMode.OriginalAndTranslation, LyricsCombineMode.Mixed);
    }

    /// <summary>
    /// 统一清洗歌词换行格式，避免接口返回中混用 <c>\r\n</c> 与 <c>\n</c>。
    /// </summary>
    private static string NormalizeLyrics(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }
}

/// <summary>
/// 网易云音乐接口封装。
/// 负责歌曲 ID 提取、详情获取、歌词获取与关键字搜索。
/// </summary>
internal static class NetEaseLyricsApi
{
    private const string NetEaseSource = "网易云";  // 网易云
    private static readonly Regex SongIdRegex = new(@"(?:[?&]id=|^)(\d{5,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// 从输入文本中提取网易云歌曲 ID。
    /// 支持纯数字 ID，也支持带 <c>?id=</c> 的完整链接。
    /// </summary>
    public static bool TryExtractSongId(string? input, out long songId)
    {
        songId = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        if (long.TryParse(text, out songId))
            return true;

        var match = SongIdRegex.Match(text);
        return match.Success && long.TryParse(match.Groups[1].Value, out songId);
    }

    /// <summary>
    /// 按歌曲 ID 获取完整歌曲对象，包括详情与歌词。
    /// </summary>
    public static async Task<SearchSongItem> GetSongAsync(long songId)
    {
        var detailTask = GetSongDetailAsync(songId);
        var lyricsTask = GetLyricsAsync(songId);

        await Task.WhenAll(detailTask, lyricsTask);

        var song = await detailTask;
        var (original, translated) = await lyricsTask;
        song.SetLyrics(original, translated);
        return song;
    }

    /// <summary>
    /// 若目标歌曲尚未加载歌词，则补充拉取歌词接口。
    /// </summary>
    public static async Task EnsureLyricsLoadedAsync(SearchSongItem song)
    {
        if (song.LyricsLoaded || song.SongId <= 0)
            return;

        var (original, translated) = await GetLyricsAsync(song.SongId);
        song.SetLyrics(original, translated);
    }

    /// <summary>
    /// 使用关键字搜索网易云歌曲列表。
    /// </summary>
    public static async Task<List<SearchSongItem>> SearchSongsAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<SearchSongItem>();

        var url =
            $"https://music.163.com/api/search/get?s={Uri.EscapeDataString(keyword.Trim())}&type=1&offset=0&limit=30&total=true";

        using var json = await GetJsonAsync(url);
        if (!json.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
        {
            return new List<SearchSongItem>();
        }

        var items = new List<SearchSongItem>();
        foreach (var songElement in songs.EnumerateArray())
        {
            items.Add(ParseSong(songElement));
        }

        return items;
    }

    /// <summary>
    /// 获取单曲详情接口并解析为统一歌曲对象。
    /// </summary>
    private static async Task<SearchSongItem> GetSongDetailAsync(long songId)
    {
        var url = $"https://music.163.com/api/song/detail?ids=[{songId}]";
        using var json = await GetJsonAsync(url);

        if (!json.RootElement.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array ||
            songs.GetArrayLength() == 0)
        {
            throw new InvalidDataException("未找到歌曲详情。");  // 未找到歌曲详情。
        }

        var song = ParseSong(songs[0]);
        if (song.SongId != 0 && song.SongId != songId)
            throw new InvalidDataException("歌曲详情返回的 ID 与请求 ID 不一致。");  // 歌曲详情返回的 ID 与请求 ID 不一致。

        return song;
    }

    /// <summary>
    /// 获取网易云歌词接口，返回原文与译文歌词文本。
    /// </summary>
    private static async Task<(string OriginalLyrics, string TranslatedLyrics)> GetLyricsAsync(long songId)
    {
        var url = $"https://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&tv=-1";
        using var json = await GetJsonAsync(url);

        var root = json.RootElement;
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 200)
            throw new InvalidDataException($"歌词接口返回错误码: {code.GetInt32()}");  // 歌词接口返回错误码

        var original = ReadNestedString(root, "lrc", "lyric");
        var translated = ReadNestedString(root, "tlyric", "lyric");
        return (original, translated);
    }

    /// <summary>
    /// 将接口返回的歌曲 JSON 解析成统一歌曲对象。
    /// </summary>
    private static SearchSongItem ParseSong(JsonElement songElement)
    {
        var songId = TryGetInt64(songElement, "id");
        var album = TryGetNestedElement(songElement, "album") ?? TryGetNestedElement(songElement, "al");
        var durationMs = TryGetInt64(songElement, "duration");
        if (durationMs <= 0)
            durationMs = TryGetInt64(songElement, "dt");

        var publishTime = album.HasValue ? TryGetInt64(album.Value, "publishTime") : 0;

        return new SearchSongItem
        {
            SongId = songId, // 歌曲 ID
            SourceId = "netease",
            SongSource = NetEaseSource, // 来源
            SongName = ReadString(songElement, "name"), // 歌曲名称
            Singer = ReadArtists(songElement), // 歌手
            Album = album.HasValue ? ReadString(album.Value, "name") : string.Empty, // 专辑名称
            SongDurationSeconds = durationMs > 0 ? durationMs / 1000.0 : 0, // 歌曲时长（秒）
            SongDuration = durationMs > 0 ? FormatDuration(durationMs / 1000.0) : string.Empty, // 歌曲时长（mm:ss）
            PublishDate = publishTime > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(publishTime).ToString("yyyy-MM-dd") : string.Empty, // 发布日期
            SongLink = songId > 0 ? $"https://music.163.com/#/song?id={songId}" : string.Empty // 歌曲链接
        };
    }

    /// <summary>
    /// 读取歌手数组并拼接成界面展示文本。
    /// </summary>
    private static string ReadArtists(JsonElement songElement)
    {
        var artists = TryGetArray(songElement, "artists") ?? TryGetArray(songElement, "ar");
        if (artists == null)
            return string.Empty;

        return string.Join(" / ",
            artists.Value.EnumerateArray()
                .Select(artist => ReadString(artist, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name)));
    }

    /// <summary>
    /// 将秒数转换为 <c>mm:ss</c> 文本。
    /// </summary>
    private static string FormatDuration(double seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(safeSeconds);
        var totalMinutes = (int)timeSpan.TotalMinutes;
        return $"{totalMinutes:D2}:{timeSpan.Seconds:D2}";
    }

    /// <summary>
    /// 发起 GET 请求并把返回内容解析为 JSON 文档。
    /// </summary>
    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>
    /// 创建网易云接口使用的共享 HttpClient。
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
            UseProxy = false
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        client.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

        return client;
    }

    // 读取嵌套属性的字符串值，避免层级较深时重复写 TryGetProperty。
    private static string ReadNestedString(JsonElement element, string parentProperty, string propertyName)
    {
        return element.TryGetProperty(parentProperty, out var parent) && parent.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    // 读取字符串值
    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    // 读取整数值
    private static long TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;

        return value.TryGetInt64(out var result) ? result : 0;
    }

    // 读取嵌套元素值
    private static JsonElement? TryGetNestedElement(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    // 读取数组值
    private static JsonElement? TryGetArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value;
    }
}

/// <summary>
/// QQ 音乐接口封装。
/// 负责从 <c>songDetail</c> 链接或 <c>songmid</c> 提取歌曲标识，并拉取详情与歌词。
/// </summary>
internal static class QqMusicApi
{
    private const string QqSource = "QQ音乐";  // QQ音乐
    private static readonly Regex SongMidRegex = new(@"songDetail/([A-Za-z0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PlainSongMidRegex = new(@"^[A-Za-z0-9]{10,20}$", RegexOptions.Compiled);
    private static readonly HttpClient Http = CreateHttpClient();

    /// <summary>
    /// 从完整链接或纯 <c>songmid</c> 文本中提取 QQ 音乐歌曲标识。
    /// </summary>
    public static bool TryExtractSongMid(string? input, out string songMid)
    {
        songMid = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        var linkMatch = SongMidRegex.Match(text);
        if (linkMatch.Success)
        {
            songMid = linkMatch.Groups[1].Value;
            return true;
        }

        if (PlainSongMidRegex.IsMatch(text))
        {
            songMid = text;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 根据 <c>songmid</c> 获取完整歌曲对象，包括详情与歌词。
    /// </summary>
    public static async Task<SearchSongItem> GetSongAsync(string songMid)
    {
        var detailTask = GetSongDetailAsync(songMid);
        var lyricsTask = GetLyricsAsync(songMid);

        await Task.WhenAll(detailTask, lyricsTask);

        var song = await detailTask;
        var (original, translated) = await lyricsTask;
        song.SetLyrics(original, translated);
        return song;
    }

    /// <summary>
    /// 若歌曲尚未加载歌词，则追加请求歌词接口。
    /// </summary>
    public static async Task EnsureLyricsLoadedAsync(SearchSongItem song)
    {
        if (song.LyricsLoaded || string.IsNullOrWhiteSpace(song.SongMid))
            return;

        var (original, translated) = await GetLyricsAsync(song.SongMid);
        song.SetLyrics(original, translated);
    }

    /// <summary>
    /// 获取 QQ 音乐歌曲详情接口。
    /// </summary>
    private static async Task<SearchSongItem> GetSongDetailAsync(string songMid)
    {
        var url = $"https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg?songmid={Uri.EscapeDataString(songMid)}&tpl=yqq_song_detail&format=json";
        using var json = await GetJsonAsync(url);

        if (!json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() == 0)
        {
            throw new InvalidDataException("未找到 QQ 音乐歌曲详情。");  // 未找到 QQ 音乐歌曲详情。
        }

        var songElement = data[0];
        var topLevelPublishDate = ReadString(songElement, "time_public");
        var album = TryGetNestedElement(songElement, "album");
        var albumPublishDate = album.HasValue ? ReadString(album.Value, "time_public") : string.Empty;
        var intervalSeconds = TryGetInt64(songElement, "interval");
        var singer = TryGetArray(songElement, "singer");

        return new SearchSongItem
        {
            SongId = TryGetInt64(songElement, "id"),
            SongMid = ReadString(songElement, "mid"),
            SourceId = "qq",
            SongSource = QqSource,
            SongName = FirstNonEmpty(ReadString(songElement, "title"), ReadString(songElement, "name")),
            Singer = singer == null
                ? string.Empty
                : string.Join(" / ", singer.Value.EnumerateArray()
                    .Select(item => ReadString(item, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))),
            Album = album.HasValue ? FirstNonEmpty(ReadString(album.Value, "name"), ReadString(album.Value, "title")) : string.Empty,
            SongDurationSeconds = intervalSeconds > 0 ? intervalSeconds : 0,
            SongDuration = intervalSeconds > 0 ? FormatDuration(intervalSeconds) : string.Empty,
            PublishDate = FirstNonEmpty(topLevelPublishDate, albumPublishDate),
            SongLink = $"https://y.qq.com/n/ryqq/songDetail/{songMid}"
        };
    }

    /// <summary>
    /// 获取 QQ 音乐歌词接口。
    /// </summary>
    private static async Task<(string OriginalLyrics, string TranslatedLyrics)> GetLyricsAsync(string songMid)
    {
        var url = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={Uri.EscapeDataString(songMid)}&format=json&nobase64=1";
        using var json = await GetJsonAsync(url);
        var root = json.RootElement;

        var code = TryGetInt64(root, "code");
        if (code != 0)
            throw new InvalidDataException($"QQ 音乐歌词接口返回错误码: {code}");  // QQ 音乐歌词接口返回错误码

        var lyric = DecodeQqLyrics(ReadString(root, "lyric"));
        var trans = DecodeQqLyrics(ReadString(root, "trans"));
        return (lyric, trans);
    }

    /// <summary>
    /// 对 QQ 接口返回的歌词文本做解码。
    /// 某些场景下返回的是 Base64，某些场景则已经是明文。
    /// </summary>
    private static string DecodeQqLyrics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Contains('[') || trimmed.Contains('\n'))
            return trimmed;

        try
        {
            var bytes = Convert.FromBase64String(trimmed);
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            return decoded.Trim();
        }
        catch
        {
            return trimmed;
        }
    }

    /// <summary>
    /// 发起 GET 请求并解析为 JSON 文档。
    /// </summary>
    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>
    /// 创建 QQ 音乐接口使用的共享 HttpClient。
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip, // 自动解压 Deflate 和 GZip 压缩
            UseProxy = false // 不使用代理
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // 设置请求头
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        client.DefaultRequestHeaders.Referrer = new Uri("https://y.qq.com/portal/player.html");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        return client;
    }

    /// <summary>
    /// 返回第一个非空字符串。
    /// </summary>
    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    // 读取字符串值
    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    // 读取整数值
    private static long TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;

        return value.TryGetInt64(out var result) ? result : 0;
    }

    // 读取嵌套元素值
    private static JsonElement? TryGetNestedElement(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    // 读取数组值
    private static JsonElement? TryGetArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value;
    }

    /// <summary>
    /// 将秒数转换为 <c>mm:ss</c> 文本。
    /// </summary>
    private static string FormatDuration(long seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(safeSeconds);
        var totalMinutes = (int)timeSpan.TotalMinutes;
        return $"{totalMinutes:D2}:{timeSpan.Seconds:D2}";
    }
}

/// <summary>
/// 在线歌词格式化工具。
/// 负责 LRC 解析、双语排版与 MMT 脚本块生成。
/// </summary>
internal static class OnlineLyricsFormatter
{
    private static readonly Regex TimeTagRegex = new(@"\[(\d{1,2}:\d{2}(?:\.\d{1,3})?)\]", RegexOptions.Compiled);

    /// <summary>
    /// 把整段 LRC 文本解析为时间行集合。
    /// </summary>
    public static List<LrcLine> ParseLrc(string? lyricText)
    {
        var result = new List<LrcLine>();
        if (string.IsNullOrWhiteSpace(lyricText))
            return result;

        var lines = lyricText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var order = 0;

        foreach (var sourceLine in lines)
        {
            var line = sourceLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var matches = TimeTagRegex.Matches(line);
            if (matches.Count == 0)
            {
                result.Add(new LrcLine(null, null, line.Trim(), line.Trim(), order++));
                continue;
            }

            var text = TimeTagRegex.Replace(line, string.Empty).Trim();
            foreach (Match match in matches)
            {
                var tag = NormalizeTimeTag(match.Groups[1].Value);
                if (!TryParseTimeTag(tag, out var seconds))
                    continue;

                result.Add(new LrcLine(seconds, tag, text, BuildTimedLine(tag, text), order++));
            }
        }

        return result;
    }

    /// <summary>
    /// 根据显示模式与双语组合模式生成最终显示文本。
    /// </summary>
    public static string FormatLyrics(SearchSongItem song, LyricsDisplayMode displayMode, LyricsCombineMode combineMode)
    {
        var original = song.OriginalLyrics.Trim();
        var translated = song.TranslatedLyrics.Trim();

        return displayMode switch
        {
            LyricsDisplayMode.OriginalOnly => !string.IsNullOrWhiteSpace(original) ? original : "暂无原文歌词。",  // 暂无原文歌词。
            LyricsDisplayMode.TranslationOnly => !string.IsNullOrWhiteSpace(translated) ? translated : "暂无译文歌词。",  // 暂无译文歌词。
            _ => combineMode switch
            {
                LyricsCombineMode.Mixed => BuildMixedLyrics(song),
                LyricsCombineMode.Merged => BuildMergedLyrics(song),
                LyricsCombineMode.Independent => BuildIndependentLyrics(song),
                _ => BuildMixedLyrics(song)
            }
        };
    }

    /// <summary>
    /// 构建插入到 MMT 脚本中的字幕块集合。
    /// </summary>
    public static List<LyricScriptBlock> BuildScriptBlocks(
        SearchSongItem song,
        LyricsDisplayMode displayMode,
        LyricsCombineMode combineMode)
    {
        var frames = BuildTimedFrames(song);
        var blocks = new List<LyricScriptBlock>();
        if (frames.Count == 0)
            return blocks;

        for (int i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var textLines = BuildScriptLines(frame, displayMode, combineMode);
            if (textLines.Count == 0)
                continue;

            var start = frame.TimeSeconds;
            var end = i + 1 < frames.Count
                ? frames[i + 1].TimeSeconds
                : song.SongDurationSeconds > frame.TimeSeconds
                    ? song.SongDurationSeconds
                    : frame.TimeSeconds + 3.0;

            if (end <= start)
                end = start + 3.0;

            blocks.Add(new LyricScriptBlock
            {
                StartSeconds = start,
                EndSeconds = end,
                TextLines = textLines
            });
        }

        return blocks;
    }

    /// <summary>
    /// 将字幕块集合格式化为最终插入脚本的文本。
    /// </summary>
    public static string FormatScriptBlocks(IEnumerable<LyricScriptBlock> blocks, LyricsTimeCodeFormat format)
    {
        var lines = new List<string>();
        foreach (var block in blocks)
        {
            var timeLine = format == LyricsTimeCodeFormat.Standard
                ? $"@{FormatStandardTime(block.StartSeconds)} - {FormatStandardTime(block.EndSeconds)}"
                : $"@{FormatSeconds(block.StartSeconds)},{FormatSeconds(block.EndSeconds - block.StartSeconds)}";

            lines.Add(timeLine);
            lines.AddRange(block.TextLines.Where(line => !string.IsNullOrWhiteSpace(line)));
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    // 构建混合歌词
    private static string BuildMixedLyrics(SearchSongItem song)
    {
        var output = new List<string>();
        output.AddRange(song.OriginalLyricLines.Where(line => !line.IsTimed).Select(line => line.RawLine));

        foreach (var frame in BuildTimedFrames(song))
        {
            output.AddRange(frame.OriginalTexts.Select(text => BuildTimedLine(frame.TimeTag, text)));
            output.AddRange(frame.TranslatedTexts.Select(text => BuildTimedLine(frame.TimeTag, text)));
        }

        return output.Count > 0 ? string.Join(Environment.NewLine, output) : "暂无歌词。";  // 暂无歌词。
    }

    // 构建合并歌词
    private static string BuildMergedLyrics(SearchSongItem song)
    {
        var output = new List<string>();
        output.AddRange(song.OriginalLyricLines.Where(line => !line.IsTimed).Select(line => line.RawLine));

        foreach (var frame in BuildTimedFrames(song))
        {
            var count = Math.Max(frame.OriginalTexts.Count, frame.TranslatedTexts.Count);
            for (int i = 0; i < count; i++)
            {
                var merged = JoinText(frame.OriginalTexts.ElementAtOrDefault(i), frame.TranslatedTexts.ElementAtOrDefault(i));
                if (!string.IsNullOrWhiteSpace(merged))
                    output.Add(BuildTimedLine(frame.TimeTag, merged));
            }
        }

        return output.Count > 0 ? string.Join(Environment.NewLine, output) : "暂无歌词。";  // 暂无歌词。
    }

    // 构建独立歌词
    private static string BuildIndependentLyrics(SearchSongItem song)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(song.OriginalLyrics))
            sections.Add(song.OriginalLyrics.Trim());

        if (!string.IsNullOrWhiteSpace(song.TranslatedLyrics))
            sections.Add(song.TranslatedLyrics.Trim());

        return sections.Count > 0 ? string.Join(Environment.NewLine, sections.Select((text, index) => index == 0 ? text : Environment.NewLine + text)) : "暂无歌词。";  // 暂无歌词。
    }

    // 构建脚本行
    private static List<string> BuildScriptLines(LyricFrame frame, LyricsDisplayMode displayMode, LyricsCombineMode combineMode)
    {
        return displayMode switch
        {
            LyricsDisplayMode.OriginalOnly => frame.OriginalTexts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
            LyricsDisplayMode.TranslationOnly => frame.TranslatedTexts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList(),
            _ => BuildBilingualScriptLines(frame, combineMode)
        };
    }

    // 构建双语脚本行
    private static List<string> BuildBilingualScriptLines(LyricFrame frame, LyricsCombineMode combineMode)
    {
        if (combineMode == LyricsCombineMode.Merged)
        {
            var mergedLines = new List<string>();
            var count = Math.Max(frame.OriginalTexts.Count, frame.TranslatedTexts.Count);
            for (int i = 0; i < count; i++)
            {
                var merged = JoinText(frame.OriginalTexts.ElementAtOrDefault(i), frame.TranslatedTexts.ElementAtOrDefault(i));
                if (!string.IsNullOrWhiteSpace(merged))
                    mergedLines.Add(merged);
            }

            return mergedLines;
        }

        // “混合”和“独立”在插入脚本时都使用多行字幕块，避免同一时间生成两个重叠字幕块。
        return frame.OriginalTexts
            .Concat(frame.TranslatedTexts)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }

    // 构建定时帧
       private static List<LyricFrame> BuildTimedFrames(SearchSongItem song)
     {
        var frameMap = new Dictionary<string, LyricFrame>(StringComparer.Ordinal);
        var order = 0;

        AddFrames(song.OriginalLyricLines, false, frameMap, ref order);
        AddFrames(song.TranslatedLyricLines, true, frameMap, ref order);

        return frameMap.Values
            .OrderBy(frame => frame.TimeSeconds)
            .ThenBy(frame => frame.Order)
            .ToList();
    }

    // 添加定时帧
    private static void AddFrames(
        IEnumerable<LrcLine> lines,
        bool translated,
        IDictionary<string, LyricFrame> frameMap,
        ref int order)
    {
        foreach (var line in lines.Where(line => line.IsTimed))
        {
            if (line.TimeTag == null || line.TimeSeconds == null)
                continue;

            if (!frameMap.TryGetValue(line.TimeTag, out var frame))
            {
                frame = new LyricFrame(line.TimeTag, line.TimeSeconds.Value, order++);
                frameMap[line.TimeTag] = frame;
            }

            if (translated)
                frame.TranslatedTexts.Add(line.Text);
            else
                frame.OriginalTexts.Add(line.Text);
        }
    }

    // 构建定时行
    private static string BuildTimedLine(string timeTag, string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? $"[{timeTag}]" : $"[{timeTag}] {text.Trim()}";
    }

    // 合并文本
    private static string JoinText(string? original, string? translated)
    {
        var parts = new[] { original?.Trim(), translated?.Trim() }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }

    // 归一化时间标签
       private static string NormalizeTimeTag(string tag)
    {
        if (!TryParseTimeTag(tag, out var seconds))
            return tag;

        return FormatTimeTag(seconds);
    }

    // 尝试解析时间标签
    private static bool TryParseTimeTag(string tag, out double seconds)
    {
        seconds = 0;
        var parts = tag.Split(':', 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secPart))
        {
            return false;
        }

        seconds = minutes * 60 + secPart;
        return true;
    }

    // 格式化时间标签
    private static string FormatTimeTag(double seconds)
    {
        var safe = Math.Max(0, seconds);
        var minutes = (int)(safe / 60);
        var secondPart = safe - minutes * 60;
        return $"{minutes:D2}:{secondPart:00.000}";
    }

    // 格式化标准时间
    private static string FormatStandardTime(double seconds)
    {
        var safe = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(safe);
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds:D3}";
    }

    // 格式化秒数
    private static string FormatSeconds(double seconds)
    {
        return Math.Max(0, seconds).ToString("0.0##", CultureInfo.InvariantCulture);
    }

    private sealed class LyricFrame
    {
        public LyricFrame(string timeTag, double timeSeconds, int order)
        {
            TimeTag = timeTag;
            TimeSeconds = timeSeconds;
            Order = order;
        }

        public string TimeTag { get; }
        public double TimeSeconds { get; }
        public int Order { get; }
        public List<string> OriginalTexts { get; } = new();
        public List<string> TranslatedTexts { get; } = new();
    }
}
