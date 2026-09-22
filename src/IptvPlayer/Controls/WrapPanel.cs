using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace IptvPlayer.Controls;

/// <summary>
/// Раскладывает элементы в строку и переносит на следующую, когда кончается ширина.
/// В WinUI своего WrapPanel нет, а карточкам плейлистов он нужен.
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var lineWidth = 0d;
        var lineHeight = 0d;
        var totalWidth = 0d;
        var totalHeight = 0d;

        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var size = child.DesiredSize;

            var needed = lineWidth == 0 ? size.Width : lineWidth + HorizontalSpacing + size.Width;

            if (needed > availableSize.Width && lineWidth > 0)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = size.Width;
                lineHeight = size.Height;
            }
            else
            {
                lineWidth = needed;
                lineHeight = Math.Max(lineHeight, size.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(
            double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width),
            totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        var y = 0d;
        var lineHeight = 0d;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;

            if (x > 0 && x + size.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));

            x += size.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(finalSize.Width, y + lineHeight);
    }
}
