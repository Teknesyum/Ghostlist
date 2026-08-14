using System.Windows.Data;
using System.Windows.Markup;

namespace Ghostlist.App;

public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Strings.Current, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
