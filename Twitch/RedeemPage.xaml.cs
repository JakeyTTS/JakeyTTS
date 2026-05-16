using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System;
using JakeyTTS.Twitch;

namespace JakeyTTS
{
    public sealed partial class RedeemPage : Page
    {
        public TwitchService ViewModel => TwitchService.Instance;
        public ObservableCollection<RedeemItem> RedeemList { get; set; }

        public RedeemPage()
        {
            this.InitializeComponent();
            var existing = ViewModel.Config.Redeems ?? new List<RedeemItem>();
            RedeemList = new ObservableCollection<RedeemItem>(existing);

            // Corregido el nombre de la tabla
            RedeemsTable.ItemsSource = RedeemList;
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Log("🔄 Requesting rewards from Twitch...");
            var remoteRewards = await ViewModel.GetCustomRewards();

            if (remoteRewards == null) return;

            foreach (var rw in remoteRewards)
            {
                if (!RedeemList.Any(r => r.Id == rw.Id))
                {
                    RedeemList.Add(rw);
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Config.Redeems = RedeemList.ToList();
            ViewModel.Config.Save();
            MainWindow.Instance?.Log("💾 Rewards saved.");
        }
    }
}