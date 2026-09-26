using Serilog.Core;
using Serilog.Events;
using StormOS.Security.Secrets;

namespace StormOS.Infrastructure.Logging;

/// <summary>Masks secrets in log event properties before they reach any sink.</summary>
public sealed class RedactionEnricher : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        List<LogEventProperty>? replacements = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            LogEventPropertyValue? replaced = null;
            if (SecretRedactor.IsSensitiveName(name))
            {
                replaced = new ScalarValue(SecretRedactor.Mask);
            }
            else if (value is ScalarValue { Value: string text })
            {
                var redacted = SecretRedactor.Redact(text);
                if (!ReferenceEquals(redacted, text) && redacted != text)
                {
                    replaced = new ScalarValue(redacted);
                }
            }
            else if (value is StructureValue structure && structure.Properties.Any(p => SecretRedactor.IsSensitiveName(p.Name)))
            {
                replaced = new StructureValue(
                    structure.Properties.Select(p => SecretRedactor.IsSensitiveName(p.Name) ? new LogEventProperty(p.Name, new ScalarValue(SecretRedactor.Mask)) : p),
                    structure.TypeTag);
            }

            if (replaced is not null)
            {
                (replacements ??= []).Add(new LogEventProperty(name, replaced));
            }
        }

        if (replacements is null)
        {
            return;
        }

        foreach (var property in replacements)
        {
            logEvent.AddOrUpdateProperty(property);
        }
    }
}

/// <summary>Adds a LogCategory property derived from the SourceContext.</summary>
public sealed class CategoryEnricher : ILogEventEnricher
{
    private readonly string _mainCategory;

    /// <summary>Initializes a new instance of the <see cref="CategoryEnricher"/> class.</summary>
    /// <param name="mainCategory">Category of the process's main log.</param>
    public CategoryEnricher(string mainCategory) => _mainCategory = mainCategory;

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);
        var source = logEvent.Properties.TryGetValue("SourceContext", out var ctx) && ctx is ScalarValue { Value: string s } ? s : null;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("LogCategory", LogCategories.Resolve(source, _mainCategory)));
    }
}
