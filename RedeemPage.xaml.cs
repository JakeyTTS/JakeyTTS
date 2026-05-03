using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System;

namespace JakeyTTS
{
    public sealed partial class RedeemPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;
        public ObservableCollection<RedeemItem> RedeemList { get; set; }

        public RedeemPage()
        {
            this.InitializeComponent();
            var existing = _service.Config.Redeems ?? new List<RedeemItem>();
            RedeemList = new ObservableCollection<RedeemItem>(existing);
            RedeemsGrid.ItemsSource = RedeemList;
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Log("🔄 Fetching rewards from Twitch...");

            // Note: You'll need to implement GetCustomRewards in your TwitchService 
            // using: GET https://api.twitch.tv/helix/channel_points/custom_rewards
            var remoteRewards = await _service.GetCustomRewards();

            if (remoteRewards == null) return;

            foreach (var rw in remoteRewards)
            {
                if (!RedeemList.Any(r => r.Id == rw.Id))
                {
                    RedeemList.Add(rw);
                }
            }
            MainWindow.Instance?.Log($"✅ Sync complete. {remoteRewards.Count} rewards found.");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _service.Config.Redeems = RedeemList.ToList();
            _service.Config.Save();
            MainWindow.Instance?.Log("💾 Rewards configuration saved.");
        }
    }
}