using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.UserActions
{
    public sealed partial class UserActionsPage : Page
    {
        // Reference to the global Twitch service
        private readonly TwitchService _service = TwitchService.Instance;

        public UserActionsPage()
        {
            this.InitializeComponent();

            // Set the DataContext so the XAML can bind directly to the Config
            this.DataContext = _service;
        }

        #region Bit Actions Logic

        private void AddBitAction_Click(object sender, RoutedEventArgs e)
        {
            // Add a new empty threshold for Bits
            _service.Config.UserActions.BitActions.Add(new UserActionItem
            {
                Threshold = 100,
                Response = "{user} cheered {bits} bits!",
                IsEnabled = true
            });
        }

        private void DeleteBitAction_Click(object sender, RoutedEventArgs e)
        {
            // Get the specific item from the button's context and remove it
            if (sender is Button btn && btn.DataContext is UserActionItem item)
            {
                _service.Config.UserActions.BitActions.Remove(item);
            }
        }

        #endregion

        #region Subscription Logic

        private void AddSubAction_Click(object sender, RoutedEventArgs e)
        {
            // Add a new threshold for total months subscribed
            _service.Config.UserActions.SubActions.Add(new UserActionItem
            {
                Threshold = 1,
                Response = "{user} subscribed for {months} months!",
                IsEnabled = true
            });
        }

        private void DeleteSubAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserActionItem item)
            {
                _service.Config.UserActions.SubActions.Remove(item);
            }
        }

        #endregion

        #region Streak Logic

        private void AddStreakAction_Click(object sender, RoutedEventArgs e)
        {
            // Add a new threshold for consecutive months (Streaks)
            _service.Config.UserActions.StreakActions.Add(new UserActionItem
            {
                Threshold = 2,
                Response = "Wow! {user} is on a {streak} month streak!",
                IsEnabled = true
            });
        }

        private void DeleteStreakAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is UserActionItem item)
            {
                _service.Config.UserActions.StreakActions.Remove(item);
            }
        }

        #endregion

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Force focus away from any active TextBox to ensure DataBinding updates the model
                this.Focus(FocusState.Programmatic);

                // Save the configuration to the JSON file
                _service.Config.Save();

                // Show a success message in the main log
                MainWindow.Instance?.Log("💾 User Actions configuration saved successfully.");

                // Visual feedback: briefly change button text or show a TeachingTip if available
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