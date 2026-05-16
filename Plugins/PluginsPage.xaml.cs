using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.Plugins
{
    public sealed partial class PluginsPage : Page, INotifyPropertyChanged
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public ObservableCollection<PluginItem> FilteredPlugins { get; } = new ObservableCollection<PluginItem>();

        // 1. Is the currently displayed list empty?
        public bool IsListEmpty => FilteredPlugins.Count == 0;

        // 2. Is a search filter currently being typed?
        public bool IsFilterActive => !string.IsNullOrWhiteSpace(SearchBox?.Text) || (ScopeFilterBox?.SelectedIndex > 0);

        // 3. Are there NO plugins at all in the database? (Hides Search bar)
        public bool IsBackendEmpty => _service.Config.Plugins == null || _service.Config.Plugins.Count == 0;

        // Panel Visibility Logic
        public bool ShowInstructions => IsBackendEmpty;
        public bool ShowNoResults => !IsBackendEmpty && IsListEmpty && IsFilterActive;

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public PluginsPage()
        {
            this.InitializeComponent();
            this.DataContext = this;
            this.Loaded += (s, e) => ApplyFilters();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();
        private void ScopeFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (_service?.Config?.Plugins == null || SearchBox == null) return;

            var searchText = SearchBox.Text?.ToLowerInvariant() ?? "";
            var selectedScope = ScopeFilterBox?.SelectedItem as string;

            // Start with all plugins from the actual backend list
            var results = _service.Config.Plugins.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchText))
                results = results.Where(p => p.Name != null && p.Name.ToLowerInvariant().Contains(searchText));

            if (!string.IsNullOrEmpty(selectedScope) && selectedScope != "All Scopes")
                results = results.Where(p => p.Subscriptions != null && p.Subscriptions.Contains(selectedScope));

            // Sync the ObservableCollection
            FilteredPlugins.Clear();
            foreach (var plugin in results) FilteredPlugins.Add(plugin);

            // Notify UI of state changes
            OnPropertyChanged(nameof(IsListEmpty));
            OnPropertyChanged(nameof(IsFilterActive));
            OnPropertyChanged(nameof(IsBackendEmpty));
            OnPropertyChanged(nameof(ShowInstructions));
            OnPropertyChanged(nameof(ShowNoResults));
        }

        private void PluginToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts && ts.DataContext is PluginItem plugin)
            {
                _service.Config.Save();
            }
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            var dataContext = (sender is FrameworkElement fe) ? fe.DataContext : null;
            if (dataContext is PluginItem plugin)
            {
                _service.Config.Plugins.Remove(plugin);
                _service.Config.Save();
                ApplyFilters();
                MainWindow.Instance?.Log($"🗑️ Plugin '{plugin.Name}' removed.");
            }
        }

        private void BuildPlugin_Click(object sender, RoutedEventArgs e)
            => this.Frame.Navigate(typeof(PluginDocsPage));
    }
}