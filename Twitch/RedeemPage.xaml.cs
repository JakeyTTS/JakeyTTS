using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
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
        public TwitchService ViewModel => TwitchService.Instance;
        public ObservableCollection<RedeemItem> RedeemList { get; set; }
        public ObservableCollection<TriggerOption> AvailableTriggers { get; } = new();
        public CommandCondition[] Conditions { get; } = (CommandCondition[])Enum.GetValues(typeof(CommandCondition));

        public RedeemPage()
        {
            this.InitializeComponent();
            var existing = ViewModel.Config.Redeems ?? new List<RedeemItem>();
            RedeemList = new ObservableCollection<RedeemItem>(existing);

            AvailableTriggers.Add(new TriggerOption { Id = "None", Name = "None" });
            AvailableTriggers.Add(new TriggerOption { Id = "All", Name = "All" });
            if (ViewModel.Config.Plugins != null)
            {
                foreach (var p in ViewModel.Config.Plugins)
                {
                    if (p.Subscriptions != null && p.Subscriptions.Contains("redeems"))
                    {
                        if (p.Triggers != null && p.Triggers.Count > 0)
                        {
                            foreach (var trigger in p.Triggers)
                            {
                                AvailableTriggers.Add(new TriggerOption { Id = trigger, Name = $"{trigger} ({p.Name})" });
                            }
                        }
                        else
                        {
                            AvailableTriggers.Add(new TriggerOption { Id = p.Id, Name = p.Name });
                        }
                    }
                }
            }

            // Fixed name of the table mapping by removing RedeemsTable reference
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Log("🔄 Requesting rewards from Twitch...");
            var remoteRewards = await ViewModel.GetCustomRewards();

            if (remoteRewards == null) return;

            this.DispatcherQueue.TryEnqueue(() => {
                try
                {
                    foreach (var rw in remoteRewards)
                    {
                        if (!RedeemList.Any(r => r.Id == rw.Id))
                        {
                            var newItem = new RedeemItem { Id = rw.Id, Name = rw.Name, IsEnabled = rw.IsEnabled };
                            RedeemList.Add(newItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.Instance?.Log($"❌ Error syncing rewards: {ex.Message}");
                }
            });
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Config.Redeems = RedeemList.ToList();
            ViewModel.Config.Save();
            MainWindow.Instance?.Log("💾 Rewards saved.");
        }

        private async void ManageVariables_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VariableManagerDialog();
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
            foreach (var r in RedeemList) foreach (var a in r.Actions) a.VariableScope = a.VariableScope;
        }

        private async void LocalVariables_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RedeemItem cmd)
            {
                var dialog = new VariableManagerDialog(cmd.LocalVariables);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
                foreach (var a in cmd.Actions) a.VariableScope = a.VariableScope;
                Save_Click(null, null); // Save after dialog closes
            }
        }

        private void UpgradeRedeem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RedeemItem cmd)
            {
                cmd.UpgradeToBlocks();
                Save_Click(null, null);
            }
        }

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is RedeemItem cmd)
            {
                cmd.Actions.Add(new CommandAction { ParentItem = cmd });
                Save_Click(null, null);
            }
        }

        private void DeleteAction_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var btn = sender as Microsoft.UI.Xaml.Controls.Button;
            if (btn?.DataContext is CommandAction action)
            {
                // Find parent command
                var cmd = RedeemList.FirstOrDefault(c => c.Actions.Contains(action));
                if (cmd != null)
                {
                    cmd.Actions.Remove(action);
                    Save_Click(null, null);
                }
            }
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            Save_Click(null, null);
            base.OnNavigatedFrom(e);
        }
    }
}
