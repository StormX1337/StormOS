using System.Globalization;
using StormOS.Core.Frames;

namespace StormOS.Performance.Frames;

/// <summary>
/// Header-driven parser for PresentMon CSV output. Column positions are taken from the header so the parser works
/// with PresentMon 1.x ("MsBetweenPresents", "TimeInSeconds", "Dropped") and 2.x ("FrameTime", "CPUStartTime",
/// "MsUntilDisplayed", "MsGPUBusy") output.
/// </summary>
public sealed class PresentMonCsvParser
{
    private int _processId = -1;
    private int _betweenPresents = -1;
    private int _frameTime = -1;
    private int _cpuStart = -1;
    private int _timeInSeconds = -1;
    private int _cpuBusy = -1;
    private int _cpuWait = -1;
    private int _gpuBusy = -1;
    private int _dropped = -1;
    private int _untilDisplayed = -1;
    private int _columns;
    private double? _previousCpuStartMs;

    /// <summary>Gets a value indicating whether a header was parsed.</summary>
    public bool HasHeader => _columns > 0;

    /// <summary>Parses a header line.</summary>
    /// <param name="line">The line.</param>
    /// <returns><see langword="true"/> when the line is a PresentMon header.</returns>
    public bool TryParseHeader(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("ProcessID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var names = line.Split(',');
        int Find(string name) => Array.FindIndex(names, n => string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase));

        _processId = Find("ProcessID");
        _betweenPresents = Find("MsBetweenPresents");
        _frameTime = Find("FrameTime");
        _cpuStart = Find("CPUStartTime");
        _timeInSeconds = Find("TimeInSeconds");
        _cpuBusy = Find("MsCPUBusy");
        _cpuWait = Find("MsCPUWait");
        _gpuBusy = Find("MsGPUBusy") is >= 0 and var busy ? busy : Find("MsGPUActive");
        _dropped = Find("Dropped");
        _untilDisplayed = Find("MsUntilDisplayed");
        _columns = names.Length;
        _previousCpuStartMs = null;
        return _processId >= 0 && (_betweenPresents >= 0 || _frameTime >= 0 || _cpuStart >= 0 || (_cpuBusy >= 0 && _cpuWait >= 0));
    }

    /// <summary>Parses a data row.</summary>
    /// <param name="line">The line.</param>
    /// <param name="sample">The frame sample.</param>
    /// <param name="processId">The process id of the row.</param>
    /// <returns><see langword="true"/> when the row produced a frame.</returns>
    public bool TryParseRow(string line, out FrameSample sample, out int processId)
    {
        sample = default;
        processId = 0;
        if (!HasHeader || string.IsNullOrEmpty(line))
        {
            return false;
        }

        var fields = line.Split(',');
        if (fields.Length < _columns || !int.TryParse(fields[_processId], NumberStyles.Integer, CultureInfo.InvariantCulture, out processId))
        {
            return false;
        }

        var cpuStart = Number(fields, _cpuStart);
        double? frameTime = Number(fields, _betweenPresents) ?? Number(fields, _frameTime);
        if (frameTime is null && cpuStart is { } start && _previousCpuStartMs is { } previous)
        {
            frameTime = start - previous;
        }

        if (frameTime is null && Number(fields, _cpuBusy) is { } busy && Number(fields, _cpuWait) is { } wait)
        {
            frameTime = busy + wait;
        }

        if (cpuStart is not null)
        {
            _previousCpuStartMs = cpuStart;
        }

        if (frameTime is not > 0)
        {
            return false;
        }

        var timestamp = Number(fields, _timeInSeconds) ?? (cpuStart / 1000.0) ?? 0;
        bool? dropped = null;
        if (_dropped >= 0)
        {
            dropped = fields[_dropped].Trim() == "1";
        }
        else if (_untilDisplayed >= 0)
        {
            dropped = string.Equals(fields[_untilDisplayed].Trim(), "NA", StringComparison.OrdinalIgnoreCase);
        }

        sample = new FrameSample(timestamp, frameTime.Value, Number(fields, _cpuBusy), Number(fields, _gpuBusy), dropped);
        return true;
    }

    private static double? Number(string[] fields, int index) =>
        index >= 0 && index < fields.Length && double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;
}
