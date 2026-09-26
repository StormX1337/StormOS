using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace StormOS.App.Controls;

/// <summary>
/// Lightweight time-series chart: gridlines, a filled area and a polyline, redrawn only when values or size change.
/// Missing values (null) break the line instead of being drawn as zero.
/// </summary>
public sealed partial class LineChart : Grid
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double?>), typeof(LineChart), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(LineChart), new PropertyMetadata(double.NaN, OnChanged));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(LineChart), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(LineChart), new PropertyMetadata(string.Empty, OnChanged));

    private readonly Canvas _canvas = new();

    public LineChart()
    {
        MinHeight = 80;
        Children.Add(_canvas);
        SizeChanged += (_, _) => Redraw();
    }

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Gets or sets the fixed maximum (NaN = automatic).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LineChart)d).Redraw();

    private static Brush Themed(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void Redraw()
    {
        _canvas.Children.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20)
        {
            return;
        }

        var values = Values ?? [];
        var measured = values.Where(v => v.HasValue && double.IsFinite(v.Value)).Select(v => v!.Value).ToList();
        var max = double.IsNaN(Maximum) ? NiceMaximum(measured.Count > 0 ? measured.Max() : 1) : Maximum;
        var grid = Themed("StormChartGridBrush");
        var label = Themed("StormTextTertiaryBrush");

        for (var i = 0; i <= 4; i++)
        {
            var y = height * i / 4.0;
            _canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1 });
        }

        var top = new TextBlock { Text = max.ToString("0.#", CultureInfo.CurrentCulture) + Unit, FontSize = 10, Foreground = label };
        Canvas.SetLeft(top, 4);
        Canvas.SetTop(top, 2);
        _canvas.Children.Add(top);

        if (values.Count < 2 || measured.Count == 0)
        {
            var empty = new TextBlock { Text = measured.Count == 0 ? "No data" : string.Empty, FontSize = 12, Foreground = label };
            Canvas.SetLeft(empty, (width / 2) - 24);
            Canvas.SetTop(empty, (height / 2) - 8);
            _canvas.Children.Add(empty);
            return;
        }

        var stroke = Stroke ?? Themed("StormAccentBrush");
        var step = width / (values.Count - 1);
        var segment = new PointCollection();
        void Flush()
        {
            if (segment.Count < 2)
            {
                segment = new PointCollection();
                return;
            }

            var fill = new PointCollection { new Point(segment[0].X, height) };
            foreach (var p in segment)
            {
                fill.Add(p);
            }

            fill.Add(new Point(segment[^1].X, height));
            _canvas.Children.Add(new Polygon { Points = fill, Fill = stroke, Opacity = 0.12 });
            _canvas.Children.Add(new Polyline { Points = segment, Stroke = stroke, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
            segment = new PointCollection();
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is { } value && double.IsFinite(value))
            {
                segment.Add(new Point(i * step, height - (Math.Clamp(value / max, 0, 1) * (height - 2))));
            }
            else
            {
                Flush();
            }
        }

        Flush();
    }

    private static double NiceMaximum(double value)
    {
        if (value <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var factor in new[] { 1, 2, 2.5, 5, 10 })
        {
            if (value <= factor * magnitude)
            {
                return factor * magnitude;
            }
        }

        return 10 * magnitude;
    }
}
