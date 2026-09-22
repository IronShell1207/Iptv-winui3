using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace IptvPlayer.Controls;

/// <summary>
/// Векторная иконка: рисует контур из словаря Styles/Icons.xaml в сетке 24×24.
/// Цвет берётся из Foreground, поэтому иконка следует теме и состоянию
/// кнопки-родителя — в отличие от иконочного шрифта.
/// </summary>
public sealed partial class SvgIcon : UserControl
{
    private const double DesignSize = 24;

    private static readonly ConcurrentDictionary<string, string> PathData =
        new(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string>? _filledIcons;

    private readonly Grid _canvas = new() { Width = DesignSize, Height = DesignSize };
    private bool _updateQueued;

    public SvgIcon()
    {
        IsTabStop = false;
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };

        // Foreground меняется со сменой темы и состояния кнопки-родителя
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => Update());
        Loaded += (_, _) => Apply();
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SvgIcon), new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Имя иконки без префикса: «Search» для ресурса Icon.Search.</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(SvgIcon),
        new PropertyMetadata(1.7, OnVisualChanged));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool?), typeof(SvgIcon), new PropertyMetadata(null, OnVisualChanged));

    /// <summary>Заливка вместо обводки. Если не задано — берётся из списка Icon.FilledSet.</summary>
    public bool? IsFilled
    {
        get => (bool?)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SvgIcon)d).Update();

    /// <summary>
    /// Разбор пути откладывается в очередь: XamlReader нельзя вызывать,
    /// пока грузится дерево разметки, которое эту иконку и создаёт.
    /// </summary>
    private void Update()
    {
        if (_updateQueued) return;
        _updateQueued = true;

        var queue = DispatcherQueue;
        if (queue is null || !queue.TryEnqueue(Apply))
            _updateQueued = false;   // до подключения к дереву обновимся в Loaded
    }

    private void Apply()
    {
        _updateQueued = false;

        var glyph = Glyph;
        _canvas.Children.Clear();

        if (string.IsNullOrWhiteSpace(glyph)) return;

        var data = Resolve(glyph);
        if (data.Length == 0) return;

        var path = BuildPath(data);
        if (path is null) return;

        if (IsFilled ?? FilledIcons.Contains(glyph))
        {
            path.Fill = Foreground;
        }
        else
        {
            path.Stroke = Foreground;
            path.StrokeThickness = StrokeThickness;
            path.StrokeLineJoin = PenLineJoin.Round;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
        }

        _canvas.Children.Add(path);
    }

    private static string Resolve(string glyph)
        => PathData.GetOrAdd(glyph, static key =>
            Application.Current.Resources.TryGetValue($"Icon.{key}", out var value) && value is string s
                ? s
                : string.Empty);

    /// <summary>
    /// Сокращённый синтаксис пути (как в SVG) понимает только разметка,
    /// поэтому фигуру собирает XamlReader. Geometry привязана к своему Path
    /// и другому элементу не отдаётся — поэтому создаём Path целиком.
    /// </summary>
    private static Path? BuildPath(string data)
    {
        try
        {
            const string ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var xaml = $"""<Path xmlns="{ns}" Data="{System.Security.SecurityElement.Escape(data)}" />""";
            return XamlReader.Load(xaml) as Path;
        }
        catch (Exception)
        {
            return null;   // кривой путь не должен ронять экран
        }
    }

    private static HashSet<string> FilledIcons
    {
        get
        {
            if (_filledIcons is not null) return _filledIcons;

            _filledIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Application.Current.Resources.TryGetValue("Icon.FilledSet", out var value) &&
                value is string list)
            {
                foreach (var name in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    _filledIcons.Add(name.Trim());
            }

            return _filledIcons;
        }
    }
}
