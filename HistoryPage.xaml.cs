using JakeyTTS.Twitch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;

namespace JakeyTTS
{
    public sealed partial class HistoryPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        // Exposing the collection for x:Bind in XAML
        public ObservableCollection<TtsEntry> History => _service.History;

        public HistoryPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Handles the replay button click. 
        /// Sends the message back to the TTS engine.
        /// </summary>
        private async void Replay_Click(object sender, RoutedEventArgs e)
        {
            // Extract the TtsEntry from the button's data context
            if (sender is Button btn && btn.DataContext is TtsEntry entry)
            {
                MainWindow.Instance?.Log($"🔄 Replaying message from {entry.User}...");

                // We reuse the standard process method
                await _service.ProcessAndSpeak(entry.Message);
            }
        }
    }
}