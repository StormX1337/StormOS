namespace StormOS.Windows.Startup;

/// <summary>Extracts the executable from Run-key style command lines.</summary>
public static class CommandLineParser
{
    /// <summary>Returns the executable portion of a command line with environment variables expanded.</summary>
    /// <param name="commandLine">Command line, for example "\"C:\Program Files\App\app.exe\" --minimized".</param>
    /// <returns>The executable path, or <see langword="null"/> when none could be determined.</returns>
    public static string? ExtractExecutable(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        foreach (var extension in new[] { ".exe", ".com", ".bat", ".cmd", ".lnk" })
        {
            var index = text.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return text[..(index + extension.Length)];
            }
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? text[..space] : text;
    }
}
