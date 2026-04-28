using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MMTGui.Avalonia;

internal static class DotNetRuntimeChecker
{
    private const string RequiredMajorVersion = "8";

    private static readonly string DownloadBaseUrl =
        "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.26/windowsdesktop-runtime-8.0.26-win-";

    public static bool CheckAndPromptInstall()
    {
        if (IsDesktopRuntimeInstalled())
            return true;

        var arch = GetArchitectureSuffix();
        if (arch == null)
            return true;

        var downloadUrl = DownloadBaseUrl + arch + ".exe";
        var fileName = Path.GetFileName(downloadUrl);
        var savePath = Path.Combine(AppContext.BaseDirectory, fileName);

        var msg = "当前系统没有安装 .NET 8.0 Desktop Runtime，将在下载安装...";
        ShowMessageBox(msg, "MMT Subtitle Editor");

        try
        {
            DownloadRuntime(downloadUrl, savePath).GetAwaiter().GetResult();

            if (File.Exists(savePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = savePath,
                    UseShellExecute = true
                });

                return false;
            }
        }
        catch (Exception ex)
        {
            ShowMessageBox(
                $"下载失败: {ex.Message}\n\n请手动下载安装:\n{downloadUrl}",
                "MMT Subtitle Editor");
        }

        return false;
    }

    private static bool IsDesktopRuntimeInstalled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-runtimes",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("Microsoft.WindowsDesktop", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var version = parts[1];
                if (version.StartsWith(RequiredMajorVersion + ".", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetArchitectureSuffix()
    {
        var arch = RuntimeInformation.OSArchitecture;
        return arch switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            _ => null
        };
    }

    private static async Task DownloadRuntime(string url, string savePath)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(savePath);

        await stream.CopyToAsync(fileStream);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, int type);

    private static void ShowMessageBox(string text, string caption)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, 0x40);
        }
        catch
        {
            Console.WriteLine(text);
        }
    }
}
