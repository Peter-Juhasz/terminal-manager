using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Templates the items of the navigation menu: the projects, and the footer items.
/// </summary>
public sealed partial class NavigationMenuTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SessionTemplate { get; set; }

    public DataTemplate? NewProjectTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        ProjectSessionViewModel => SessionTemplate,
        NewProjectMenuItem => NewProjectTemplate,
        _ => null,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
