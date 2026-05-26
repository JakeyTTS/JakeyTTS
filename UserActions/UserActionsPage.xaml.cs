using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.UserActions
{
    public sealed partial class UserActionsPage : Page
    {
        // TwitchService keeps data collection and configuration logic centralized, so we can bind directly to it for simplicity
        private readonly TwitchService _service = TwitchService.Instance;

        public UserActionsPage()
        {
            this.InitializeComponent();
            this.DataContext = _service;

            CategorySelector.SelectionChanged += CategorySelector_SelectionChanged;
            UpdateSubPageVisibility("Bits"); // load subpage visibility on initial load
        }

        private void CategorySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategorySelector.SelectedItem is ListViewItem item && item.Tag is string tag)
            {
                UpdateSubPageVisibility(tag);
            }
        }

        private void UpdateSubPageVisibility(string activeTag)
        {
            if (BitsSubPage == null) return; // safety check to avoid null refs during initialization

            // Alternate visibilities of subpages
            BitsSubPage.Visibility = activeTag == "Bits" ? Visibility.Visible : Visibility.Collapsed;
            SubsSubPage.Visibility = activeTag == "Subs" ? Visibility.Visible : Visibility.Collapsed;
            StreaksSubPage.Visibility = activeTag == "Streaks" ? Visibility.Visible : Visibility.Collapsed;
            GoalsSubPage.Visibility = activeTag == "Goals" ? Visibility.Visible : Visibility.Collapsed;

            // Also toggle the guide blocks on the right side
            GuideBitsBlock.Visibility = activeTag == "Bits" ? Visibility.Visible : Visibility.Collapsed;
            GuideSubsBlock.Visibility = activeTag == "Subs" ? Visibility.Visible : Visibility.Collapsed;
            GuideStreaksBlock.Visibility = activeTag == "Streaks" ? Visibility.Visible : Visibility.Collapsed;
            GuideGoalsBlock.Visibility = activeTag == "Goals" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void PlayTest_Click(object sender, RoutedEventArgs e)
        {
            if (CategorySelector.SelectedItem is not ListViewItem item || item.Tag is not string tag) return;

            string testUser = "JakeyViewer";

            // FIXED: All calls to TtsEngine should now be awaited to ensure proper async handling and avoid potential issues with overlapping speech or unhandled exceptions.
            // This also allows the UI to remain responsive during the test playback.
            if (tag == "Bits")
            {
                var action = _service.Config.UserActions.BitActions.FirstOrDefault(a => a.IsEnabled);
                if (action != null)
                {
                    string parsed = action.Response.Replace("{user}", testUser).Replace("{bits}", action.Threshold.ToString());
                    if (action.ShouldPlayUserMessage) parsed += " Cheering from Spain!";
                    await TtsEngine.Instance.ProcessAndSpeak(parsed, "test");
                }
            }
            else if (tag == "Subs")
            {
                var action = _service.Config.UserActions.SubActions.FirstOrDefault(a => a.IsEnabled);
                if (action != null)
                {
                    string parsed = action.Response.Replace("{user}", testUser).Replace("{months}", action.Threshold.ToString());
                    if (action.ShouldPlayUserMessage) parsed += " Keep up the great streams!";
                    await TtsEngine.Instance.ProcessAndSpeak(parsed, "test");
                }
            }
            else if (tag == "Streaks")
            {
                var action = _service.Config.UserActions.StreakActions.FirstOrDefault(a => a.IsEnabled);
                if (action != null)
                {
                    string parsed = action.Response.Replace("{user}", testUser).Replace("{streak}", action.Threshold.ToString());
                    if (action.ShouldPlayUserMessage) parsed += " Best stream ever!";
                    await TtsEngine.Instance.ProcessAndSpeak(parsed, "test");
                }
            }
            else if (tag == "Goals")
            {
                if (!string.IsNullOrEmpty(_service.Config.UserActions.SubGoalReachedResponse))
                {
                    string parsed = _service.Config.UserActions.SubGoalReachedResponse.Replace("{goal_title}", "Surprise 24h Stream");
                    await TtsEngine.Instance.ProcessAndSpeak(parsed, "test");
                }
            }
        }

        private void AddBitAction_Click(object sender, RoutedEventArgs e)
        {
            _service.Config.UserActions.BitActions.Add(new UserActionItem { Threshold = 100.0, Response = "{user} cheered {bits} bits!", IsEnabled = true, ShouldPlayUserMessage = true });
        }

        private void DeleteBitAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserActionItem item) _service.Config.UserActions.BitActions.Remove(item);
        }

        private void AddSubAction_Click(object sender, RoutedEventArgs e)
        {
            _service.Config.UserActions.SubActions.Add(new UserActionItem { Threshold = 1.0, Response = "{user} subscribed for {months} months!", IsEnabled = true, ShouldPlayUserMessage = true });
        }

        private void DeleteSubAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserActionItem item) _service.Config.UserActions.SubActions.Remove(item);
        }

        private void AddStreakAction_Click(object sender, RoutedEventArgs e)
        {
            _service.Config.UserActions.StreakActions.Add(new UserActionItem { Threshold = 2.0, Response = "Wow! {user} is on a {streak} month streak!", IsEnabled = true, ShouldPlayUserMessage = true });
        }

        private void DeleteStreakAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserActionItem item) _service.Config.UserActions.StreakActions.Remove(item);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Focus(FocusState.Programmatic);
                _service.Config.Save();
                MainWindow.Instance?.Log("💾 User Actions configuration saved successfully.");

                if (sender is Button btn)
                {
                    string original = btn.Content.ToString();
                    btn.Content = "Saved!";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) => { btn.Content = original; timer.Stop(); };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"❌ Error saving actions: {ex.Message}");
            }
        }
    }
}
