using System.Text.Json;
using System.Text.Json.Serialization;

namespace StormOS.Core.Common;

/// <summary>Shared JSON serializer configuration used by IPC, persistence and profiles.</summary>
public static class StormJson
{
    /// <summary>Gets the shared serializer options (camelCase, string enums, no trailing commas tolerated for IPC).</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions(indented: false);

    /// <summary>Gets serializer options that produce indented output for files meant to be read by people.</summary>
    public static JsonSerializerOptions Indented { get; } = CreateOptions(indented: true);

    /// <summary>Gets lenient options for user editable files (comments and trailing commas allowed).</summary>
    public static JsonSerializerOptions Lenient { get; } = CreateLenient();

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32,
            AllowOutOfOrderMetadataProperties = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static JsonSerializerOptions CreateLenient()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = 32,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
