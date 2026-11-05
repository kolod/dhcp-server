using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace DhcpServer.App.Localization
{
    /// <summary>
    /// XAML markup extension for localized text: <c>Title="{l:Loc App_Title}"</c>.
    /// The text follows runtime language switches. Every user-visible string in XAML MUST use it.
    /// </summary>
    [MarkupExtensionReturnType(typeof(object))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }
    }
}
