using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace JakeyTTS
{
    public sealed partial class HistoryPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public ObservableCollection<TtsEntry> History => _service.History;

        public HistoryPage()
        {
            this.InitializeComponent();
        }

        private async void Replay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TtsEntry entry)
            {
                MainWindow.Instance?.Log($"🔄 Replaying message from {entry.User}...");

                // FIXED: Redirigido a TtsEngine
                await TtsEngine.Instance.ProcessAndSpeak(entry.Message);
            }
        }
    }
}