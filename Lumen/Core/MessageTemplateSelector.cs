using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Lumen.Core
{
    public sealed class MessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate UserTemplate { get; set; }
        public DataTemplate ModelTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            var m = item as ChatMessage;
            return m != null && m.IsUser ? UserTemplate : ModelTemplate;
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}
