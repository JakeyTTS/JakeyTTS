using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;
using System.Linq;

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
            if (this.Frame.CanGoBack) this.Frame.GoBack();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string targetName)
            {
                // Find the TextBlock by its x:Name
                var textBlock = this.FindName(targetName) as TextBlock;
                if (textBlock == null) return;

                // Extract text from Runs if necessary, or just use .Text
                string content = textBlock.Text;

                // If the TextBlock uses Inline/Runs (like the JSON examples), we iterate them
                if (string.IsNullOrEmpty(content) && textBlock.Inlines.Count > 0)
                {
                    content = string.Join("", textBlock.Inlines.Select(i =>
                        i is Run r ? r.Text : (i is LineBreak ? "\n" : "")));
                }

                // Copy to Clipboard
                var dataPackage = new DataPackage();
                dataPackage.SetText(content);
                Clipboard.SetContent(dataPackage);

                // Optional: Show a quick feedback (Visual Studio style)
                VisualStateManager.GoToState(btn, "PointerOver", true);
                MainWindow.Instance?.Log("📋 Copied to clipboard.");
            }
        }
    }
}