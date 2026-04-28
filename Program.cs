using System;
using System.Text;
using Avalonia;

namespace MMTGui.Avalonia
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (!DotNetRuntimeChecker.CheckAndPromptInstall())
                return;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
