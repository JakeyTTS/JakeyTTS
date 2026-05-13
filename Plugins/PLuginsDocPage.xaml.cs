using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.Plugins
{
    public sealed partial class PluginDocsPage : Page
    {
        public PluginDocsPage()
        {
            this.InitializeComponent();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }
}