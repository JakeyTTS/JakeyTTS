using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace JakeyTTS.Views
{
    public sealed partial class CommandsPage : Page
    {
        public TwitchService ViewModel => TwitchService.Instance;
        public ObservableCollection<CommandItem> CommandList { get; set; }
        public ObservableCollection<TriggerOption> AvailableTriggers { get; } = new();

        public CommandsPage()
        {
            this.InitializeComponent();
            var existing = ViewModel.Config.Commands ?? new System.Collections.Generic.List<CommandItem>();
            CommandList = new ObservableCollection<CommandItem>(existing);

            AvailableTriggers.Add(new TriggerOption { Id = "None", Name = "None" });
            AvailableTriggers.Add(new TriggerOption { Id = "All", Name = "All" });
            if (ViewModel.Config.Plugins != null)
            {
                foreach (var p in ViewModel.Config.Plugins)
                {
                    if (p.Subscriptions != null && p.Subscriptions.Contains("commands"))
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

            CommandsTable.ItemsSource = CommandList;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            CommandList.Add(new CommandItem { Trigger = "!neew", Response = "Hello {user}", IsEnabled = true });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (CommandsTable.SelectedItem is CommandItem selected)
            {
                CommandList.Remove(selected);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveAllButton.Focus(FocusState.Programmatic);
            ViewModel.Config.Commands = CommandList.ToList();

            ViewModel.Config.Save();

            MainWindow.Instance?.Log("💾 Commands saved successfully.");
        }
    }
}

