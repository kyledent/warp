using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace CubeAvalonia.Controls;

public partial class ValueSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Value), defaultValue: 0,
            coerce: (o, v) => CoerceValue((ValueSlider)o, v));

    public static readonly StyledProperty<double> MinValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(MinValue), defaultValue: 0);

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(MaxValue), defaultValue: 100);

    public static readonly StyledProperty<double> StepSizeProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(StepSize), defaultValue: 1);

    public static readonly StyledProperty<int> DecimalPlacesProperty =
        AvaloniaProperty.Register<ValueSlider, int>(nameof(DecimalPlaces), defaultValue: 0);

    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<ValueSlider, string>(nameof(Suffix), defaultValue: "");

    public static readonly DirectProperty<ValueSlider, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<ValueSlider, string>(nameof(DisplayText),
            o => o.DisplayText, (o, v) => o.DisplayText = v);

    private string _displayText = "0";

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double MinValue
    {
        get => GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public double StepSize
    {
        get => GetValue(StepSizeProperty);
        set => SetValue(StepSizeProperty, value);
    }

    public int DecimalPlaces
    {
        get => GetValue(DecimalPlacesProperty);
        set => SetValue(DecimalPlacesProperty, value);
    }

    public string Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public string DisplayText
    {
        get => _displayText;
        set
        {
            // Parse text input back to value
            string cleaned = value.Replace(Suffix, "").Trim();
            if (double.TryParse(cleaned, CultureInfo.InvariantCulture, out double parsed))
            {
                Value = parsed;
            }
            UpdateDisplayText();
        }
    }

    public ValueSlider()
    {
        InitializeComponent();
    }

    static ValueSlider()
    {
        ValueProperty.Changed.AddClassHandler<ValueSlider>((s, _) => s.UpdateDisplayText());
        DecimalPlacesProperty.Changed.AddClassHandler<ValueSlider>((s, _) => s.UpdateDisplayText());
        SuffixProperty.Changed.AddClassHandler<ValueSlider>((s, _) => s.UpdateDisplayText());
    }

    private void UpdateDisplayText()
    {
        string format = "F" + DecimalPlaces;
        string text = Value.ToString(format, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(Suffix))
            text += " " + Suffix;
        SetAndRaise(DisplayTextProperty, ref _displayText, text);
    }

    private static double CoerceValue(ValueSlider slider, double value)
    {
        value = Math.Max(slider.MinValue, Math.Min(slider.MaxValue, value));
        if (slider.StepSize > 0)
            value = Math.Round(value / slider.StepSize) * slider.StepSize;
        return Math.Round(value, slider.DecimalPlaces);
    }
}
