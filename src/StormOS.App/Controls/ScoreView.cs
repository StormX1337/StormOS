using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StormOS.App.Helpers;
using StormOS.Core.Scoring;

namespace StormOS.App.Controls;

/// <summary>
/// Shows a score transparently: result, rating, every input with its measured value, normalized value and weight,
/// the coverage and the formula. Unmeasured inputs are listed as such.
/// </summary>
public sealed partial class ScoreView : StackPanel
{
    public static readonly DependencyProperty BreakdownProperty = DependencyProperty.Register(nameof(Breakdown), typeof(ScoreBreakdown), typeof(ScoreView), new PropertyMetadata(null, OnChanged));
    public static readonly DependencyProperty ShowInputsProperty = DependencyProperty.Register(nameof(ShowInputs), typeof(bool), typeof(ScoreView), new PropertyMetadata(true, OnChanged));

    public ScoreView()
    {
        Spacing = 6;
        Render();
    }

    public ScoreBreakdown? Breakdown
    {
        get => (ScoreBreakdown?)GetValue(BreakdownProperty);
        set => SetValue(BreakdownProperty, value);
    }

    public bool ShowInputs
    {
        get => (bool)GetValue(ShowInputsProperty);
        set => SetValue(ShowInputsProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ScoreView)d).Render();

    private static Style? StyleOf(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;

    private void Render()
    {
        Children.Clear();
        var breakdown = Breakdown;
        if (breakdown is null)
        {
            Children.Add(new TextBlock { Text = "No score yet.", Style = StyleOf("StormCaptionStyle") });
            return;
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new TextBlock
        {
            Text = Format.Score(breakdown.Score),
            Style = StyleOf("StormLargeValueStyle"),
            Foreground = Format.ScoreBrush(breakdown.Score),
        });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = breakdown.Rating, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        label.Children.Add(new TextBlock
        {
            Text = $"Coverage {breakdown.Coverage * 100:0} % of weight measured",
            Style = StyleOf("StormCaptionStyle"),
        });
        header.Children.Add(label);
        Children.Add(header);

        if (!ShowInputs)
        {
            return;
        }

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddRow(grid, 0, "INPUT", "MEASURED", "NORMALIZED", "WEIGHT", header: true);
        var row = 1;
        foreach (var input in breakdown.Inputs)
        {
            var measured = input.RawValue is { } raw ? raw.ToString("0.##", CultureInfo.CurrentCulture) + " " + input.Unit : "Not measured";
            var normalized = input.Normalized is { } n ? n.ToString("0", CultureInfo.CurrentCulture) : "—";
            AddRow(grid, row, input.Name, measured, normalized, input.Weight.ToString("0.##", CultureInfo.CurrentCulture), header: false, input.Explanation);
            row++;
        }

        Children.Add(grid);
        Children.Add(new TextBlock
        {
            Text = breakdown.Formula,
            Style = StyleOf("StormCaptionStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
    }

    private static void AddRow(Grid grid, int row, string name, string measured, string normalized, string weight, bool header, string? tooltip = null)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] cells = [name, measured, normalized, weight];
        for (var column = 0; column < cells.Length; column++)
        {
            var text = new TextBlock
            {
                Text = cells[column],
                Style = StyleOf(header ? "StormLabelStyle" : column == 0 ? "StormBodyStyle" : "StormValueStyle"),
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (header)
            {
                text.FontSize = 11;
            }

            if (tooltip is not null)
            {
                ToolTipService.SetToolTip(text, tooltip);
            }

            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
    }
}
