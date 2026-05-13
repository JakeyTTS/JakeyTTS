using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.Plugins
{
    public sealed partial class PluginsPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        // NEW: This is the list the UI is now bound to for searching/filtering
        public ObservableCollection<PluginItem> FilteredPlugins { get; set; } = new ObservableCollection<PluginItem>();

        public PluginsPage()
        {
            this.InitializeComponent();

            // Set context so XAML can read _service.Config.Plugins if needed
            this.DataContext = _service;

            // Populate the filtered list initially with all plugins
            ApplyFilters();
        }

        // --- FILTERING LOGIC ---

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ScopeFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            // Safeguard: Make sure Config is loaded
            if (_service?.Config?.Plugins == null) return;

            var searchText = SearchBox?.Text?.ToLowerInvariant() ?? "";
            var selectedScope = ScopeFilterBox?.SelectedItem as string;

            // 1. Start with all plugins
            var results = _service.Config.Plugins.AsEnumerable();

            // 2. Filter by Search Text (checks plugin Name)
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                results = results.Where(p => p.Name != null && p.Name.ToLowerInvariant().Contains(searchText));
            }

            // 3. Filter by selected Scope
            if (!string.IsNullOrEmpty(selectedScope) && selectedScope != "All Scopes")
            {
                results = results.Where(p => p.Subscriptions != null && p.Subscriptions.Contains(selectedScope));
            }

            // 4. Update the observable collection
            FilteredPlugins.Clear();
            foreach (var plugin in results)
            {
                FilteredPlugins.Add(plugin);
            }
        }

        // --- EXISTING ACTION LOGIC ---

        private void PluginToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // If the user enables/disables a plugin, save the config immediately.
            // The PluginServer checks this config in real-time, so the change takes effect instantly.
            if (sender is ToggleSwitch ts && ts.DataContext is PluginItem plugin)
            {
                _service.Config.Save();

                string status = plugin.IsEnabled ? "Authorized" : "Revoked";
                MainWindow.Instance?.Log($"🔒 Plugin '{plugin.Name}' access {status}.");
            }
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PluginItem plugin)
            {
                // Remove it from the main list
                _service.Config.Plugins.Remove(plugin);
                _service.Config.Save();

                // NEW: Update the visual filtered list so the card disappears instantly
                ApplyFilters();

                MainWindow.Instance?.Log($"🗑️ Plugin '{plugin.Name}' removed.");
            }
        }

        private void BuildPlugin_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Navigate to the new documentation page
            this.Frame.Navigate(typeof(PluginDocsPage));
        }
    }
}