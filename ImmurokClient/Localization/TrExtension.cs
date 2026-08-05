using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace ImmurokClient.Localization;

/// <summary>
/// XAML 本地化标记扩展：<c>Text="{loc:Tr nav.device}"</c>。
/// 返回一个绑定到 <see cref="Loc"/> 索引器的 OneWay Binding，
/// 切换语言时（Loc 触发 "Item[]"）自动刷新文案。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
