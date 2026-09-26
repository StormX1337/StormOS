using System.Globalization;
using System.Text.Json;
using StormOS.Core.Common;

namespace StormOS.Cli;

/// <summary>Console output helpers (human readable or JSON).</summary>
internal static class Output
{
    public static bool Json { get; set; }

    public static void Title(string text)
    {
        if (!Json)
        {
            Console.WriteLine();
            Console.WriteLine(text.ToUpperInvariant());
            Console.WriteLine(new string('─', Math.Min(60, text.Length + 4)));
        }
    }

    public static void Row(string label, string? value)
    {
        if (!Json)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label,-26} {value ?? "Unavailable"}"));
        }
    }

    public static void Line(string text)
    {
        if (!Json)
        {
            Console.WriteLine(text);
        }
    }

    public static void Data(object value)
    {
        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, StormJson.Indented));
        }
    }

    public static int Error(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }

    public static string Reading(Reading reading, string format, string unit) =>
        reading.Value is { } value ? value.ToString(format, CultureInfo.InvariantCulture) + unit : $"Unavailable ({reading.UnavailableReason})";

    /// <summary>Asks for confirmation. Non-interactive sessions never confirm implicitly.</summary>
    public static bool Confirm(string question, bool assumeYes)
    {
        if (assumeYes)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Confirmation required. Re-run with --yes to confirm non-interactively.");
            return false;
        }

        Console.Write(question + " [y/N] ");
        var answer = Console.ReadLine();
        return string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}
