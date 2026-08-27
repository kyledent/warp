using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace WarpAvalonia.Controls;

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

    // ValueText: just the number, no suffix
    public static readonly DirectProperty<ValueSlider, string> ValueTextProperty =
        AvaloniaProperty.RegisterDirect<ValueSlider, string>(nameof(ValueText),
            o => o.ValueText, (o, v) => o.ValueText = v);

    // DisplayText: kept for backward compat (number + suffix)
    public static readonly DirectProperty<ValueSlider, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<ValueSlider, string>(nameof(DisplayText),
            o => o.DisplayText, (o, v) => o.DisplayText = v);

    // HasSuffix: for suffix TextBlock visibility
    public static readonly DirectProperty<ValueSlider, bool> HasSuffixProperty =
        AvaloniaProperty.RegisterDirect<ValueSlider, bool>(nameof(HasSuffix), o => o.HasSuffix);

    private string _valueText = "0";
    private string _displayText = "0";
    private bool _hasSuffix = false;

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

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (double.TryParse(value?.Trim(), CultureInfo.InvariantCulture, out double parsed))
                Value = parsed;
            UpdateTexts();
        }
    }

    public string DisplayText
    {
        get => _displayText;
        set
        {
            string cleaned = value?.Replace(Suffix ?? "", "").Trim() ?? "";
            if (double.TryParse(cleaned, CultureInfo.InvariantCulture, out double parsed))
                Value = parsed;
            UpdateTexts();
        }
    }

    public bool HasSuffix => !string.IsNullOrEmpty(Suffix);

    public ValueSlider()
    {
        InitializeComponent();
    }

    static ValueSlider()
    {
        ValueProperty.Changed.AddClassHandler<ValueSlider>((s, _) => s.UpdateTexts());
        DecimalPlacesProperty.Changed.AddClassHandler<ValueSlider>((s, _) => s.UpdateTexts());
        SuffixProperty.Changed.AddClassHandler<ValueSlider>((s, _) =>
        {
            s.UpdateTexts();
            s.RaisePropertyChanged(HasSuffixProperty, !s.HasSuffix, s.HasSuffix);
        });
    }

    private void UpdateTexts()
    {
        string format = "F" + DecimalPlaces;
        string numText = Value.ToString(format, CultureInfo.InvariantCulture);

        SetAndRaise(ValueTextProperty, ref _valueText, numText);

        string fullText = numText;
        if (!string.IsNullOrEmpty(Suffix))
            fullText += " " + Suffix;
        SetAndRaise(DisplayTextProperty, ref _displayText, fullText);
    }

    private static double CoerceValue(ValueSlider slider, double value)
    {
        value = Math.Max(slider.MinValue, Math.Min(slider.MaxValue, value));
        if (slider.StepSize > 0)
            value = Math.Round(value / slider.StepSize) * slider.StepSize;
        return Math.Round(value, slider.DecimalPlaces);
    }
}
