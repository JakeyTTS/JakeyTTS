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
    public partial class PluginsPage : Page, INotifyPropertyChanged
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public ObservableCollection<PluginItem> FilteredPlugins { get; } = new ObservableCollection<PluginItem>();

        public bool IsListEmpty => FilteredPlugins.Count == 0;
        public bool IsFilterActive => !string.IsNullOrWhiteSpace(SearchBox?.Text) || (ScopeFilterBox?.SelectedIndex > 0);
        public bool IsBackendEmpty => _service.Config.Plugins == null || _service.Config.Plugins.Count == 0;

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

            var results = _service.Config.Plugins.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchText))
                results = results.Where(p => p.Name != null && p.Name.ToLowerInvariant().Contains(searchText));

            if (!string.IsNullOrEmpty(selectedScope) && selectedScope != "All Scopes")
                results = results.Where(p => p.Subscriptions != null && p.Subscriptions.Contains(selectedScope));

            FilteredPlugins.Clear();
            foreach (var plugin in results) FilteredPlugins.Add(plugin);

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

                if (plugin.IsEnabled)
                {
                    if (!string.IsNullOrWhiteSpace(plugin.ExecutablePath))
                    {
                        PluginServer.Instance.LaunchPluginProcess(plugin);
                    }
                }
                else
                {
                    PluginServer.Instance.KillPluginProcess(plugin.Id);
                }
            }
        }

        private void InvisibleLaunch_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is PluginItem plugin)
            {
                plugin.LaunchInvisible = cb.IsChecked ?? false;
                _service.Config.Save();
            }
        }

        private async void LaunchManualProgram_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PluginItem plugin)
            {
                if (!string.IsNullOrWhiteSpace(plugin.ExecutablePath))
                {
                    PluginServer.Instance.LaunchPluginProcess(plugin);
                }
            }
        }

        private void DeletePlugin_Click(object sender, RoutedEventArgs e)
        {
            var dataContext = (sender is FrameworkElement fe) ? fe.DataContext : null;
            if (dataContext is PluginItem plugin)
            {
                PluginServer.Instance.KillPluginProcess(plugin.Id);
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