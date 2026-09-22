using IptvPlayer.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IptvPlayer.Controls;

/// <summary>Карточка плейлиста или карточка «добавить» — обе в одной сетке.</summary>
public sealed partial class PlaylistCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PlaylistTemplate { get; set; }

    public DataTemplate? AddTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is AddPlaylistPlaceholder ? AddTemplate : PlaylistTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
