using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.StatusBar.
/// Color-coded item grid showing processing status of all movies/tilt series.
/// Each item is a small colored rectangle: green=processed, yellow=outdated,
/// red=unprocessed, cyan=filtered, gray=deselected.
/// </summary>
public partial class ProcessingStatusBar : UserControl
{
    private List<StatusItem> _items = new();
    private int _activeIndex = -1;

    public event Action<int>? ActiveItemChanged;

    public ProcessingStatusBar()
    {
        InitializeComponent();
    }

    static ProcessingStatusBar()
    {
        BoundsProperty.Changed.AddClassHandler<ProcessingStatusBar>((s, _) => s.UpdateElements());
    }

    public void SetItems(List<StatusItem> items)
    {
        _items = items;
        TextCount.Text = $"Processed {items.FindAll(i => i.Status == ItemStatus.Processed || i.Status == ItemStatus.FilteredOut).Count}/{items.Count}.";
        UpdateElements();
    }

    public void SetActiveIndex(int index)
    {
        _activeIndex = index;
        UpdateElements();
    }

    public void UpdateElements()
    {
        CanvasItems.Children.Clear();

        double w = CanvasItems.Bounds.Width;
        double h = CanvasItems.Bounds.Height;
        if (w <= 0 || h <= 0 || _items.Count == 0) return;

        double stepWidth = Math.Max(2, w / _items.Count);
        int itemsPerRow = Math.Max(1, (int)(w / stepWidth + 0.001));
        int nRows = (_items.Count + itemsPerRow - 1) / itemsPerRow;
        double rowHeight = Math.Min(16, h / Math.Max(1, nRows));

        for (int i = 0; i < _items.Count; i++)
        {
            int row = i / itemsPerRow;
            int col = i % itemsPerRow;

            var rect = new Rectangle
            {
                Width = Math.Max(1, stepWidth - 1),
                Height = rowHeight - 1,
                Fill = StatusToBrush(_items[i].Status),
                RadiusX = 1,
                RadiusY = 1
            };

            Canvas.SetLeft(rect, col * stepWidth);
            Canvas.SetTop(rect, row * rowHeight);
            CanvasItems.Children.Add(rect);

            // Active item highlight
            if (i == _activeIndex)
            {
                var highlight = new Rectangle
                {
                    Width = Math.Max(1, stepWidth - 1),
                    Height = rowHeight - 1,
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent,
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(highlight, col * stepWidth);
                Canvas.SetTop(highlight, row * rowHeight);
                CanvasItems.Children.Add(highlight);
            }
        }
    }

    private static IBrush StatusToBrush(ItemStatus status) => status switch
    {
        ItemStatus.Processed => new SolidColorBrush(Color.Parse("#cc77cd77")),
        ItemStatus.Outdated => new SolidColorBrush(Color.Parse("#ccffc456")),
        ItemStatus.Unprocessed => new SolidColorBrush(Color.Parse("#ccff7d7d")),
        ItemStatus.FilteredOut => new SolidColorBrush(Color.Parse("#cc7edfff")),
        ItemStatus.Deselected => new SolidColorBrush(Colors.LightGray),
        _ => Brushes.Gray
    };

    /// <summary>Generate test items for PoC.</summary>
    public static List<StatusItem> GenerateTestItems(int count)
    {
        var items = new List<StatusItem>();
        var rng = new Random(42);
        var statuses = new[] { ItemStatus.Processed, ItemStatus.Processed, ItemStatus.Processed,
                               ItemStatus.Outdated, ItemStatus.Unprocessed, ItemStatus.FilteredOut, ItemStatus.Deselected };
        for (int i = 0; i < count; i++)
            items.Add(new StatusItem($"item_{i:D4}", statuses[rng.Next(statuses.Length)]));
        return items;
    }
}

public enum ItemStatus { Processed, Outdated, Unprocessed, FilteredOut, Deselected }

public struct StatusItem
{
    public string Name;
    public ItemStatus Status;
    public StatusItem(string name, ItemStatus status) { Name = name; Status = status; }
}
