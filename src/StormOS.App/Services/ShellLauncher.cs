using System.Diagnostics;

namespace StormOS.App.Services;

/// <summary>Opens folders, files in Explorer and HTTPS links. Never runs arbitrary commands.</summary>
public static class ShellLauncher
{
    /// <summary>Selects a file in Explorer.</summary>
    /// <param name="path">File path.</param>
    public static void ShowInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = Path.GetFullPath(path);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        if (File.Exists(full))
        {
            info.ArgumentList.Add("/select," + full);
        }
        else if (Directory.Exists(full))
        {
            info.ArgumentList.Add(full);
        }
        else
        {
            return;
        }

        using var _ = Process.Start(info);
    }

    /// <summary>Opens an HTTPS URL in the default browser.</summary>
    /// <param name="url">URL.</param>
    public static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
