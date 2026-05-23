using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JakeyTTS.Views
{
    public class EnumToStringConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is CommandCondition cond)
            {
                return cond switch
                {
                    CommandCondition.Always => "Always",
                    CommandCondition.IfUserProvidedMessage => "If User Provided Message",
                    CommandCondition.IfNoMessageProvided => "If No Message Provided",
                    CommandCondition.IfVariableMatch => "If Variable Matches",
                    CommandCondition.IfVariableListIsEmpty => "If Variable List is Empty",
                    CommandCondition.IfRandomChance => "If Random Chance (%)",
                    _ => value.ToString()
                };
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed partial class CommandsPage : Page
    {
        public TwitchService ViewModel => TwitchService.Instance;
        public ObservableCollection<CommandItem> CommandList { get; set; }
        public ObservableCollection<TriggerOption> AvailableTriggers { get; } = new();

        public List<CommandCondition> Conditions { get; } = Enum.GetValues(typeof(CommandCondition)).Cast<CommandCondition>().ToList();

        public ObservableCollection<string> AvailableVariables { get; } = new();

        public CommandsPage()
        {
            this.InitializeComponent();
            var existing = ViewModel.Config.Commands ?? new System.Collections.Generic.List<CommandItem>();
            foreach(var c in existing)
            {
                foreach(var a in c.Actions) a.ParentItem = c;
            }
            CommandList = new ObservableCollection<CommandItem>(existing);

            UpdateAvailableVariables();

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
        }


        private void Add_Click(object sender, RoutedEventArgs e)
        {
            CommandList.Add(new CommandItem { Trigger = "!new", Response = "Hello {user}", IsEnabled = true });
            Save_Click(null, null);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (CommandsListView.SelectedItem is CommandItem selected)
            {
                CommandList.Remove(selected);
                Save_Click(null, null);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveAllButton?.Focus(FocusState.Programmatic);
            ViewModel.Config.Commands = CommandList.ToList();
            ViewModel.Config.Save();
        }

        private async void ManageVariables_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VariableManagerDialog();
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
            foreach(var c in CommandList) foreach(var a in c.Actions) a.VariableScope = a.VariableScope;
        }

        private async void LocalVariables_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CommandItem cmd)
            {
                var dialog = new VariableManagerDialog(cmd.LocalVariables);
                dialog.XamlRoot = this.XamlRoot;
                await dialog.ShowAsync();
                foreach(var a in cmd.Actions) a.VariableScope = a.VariableScope;
                Save_Click(null, null); // Save after dialog closes
            }
        }

        private void UpgradeCommand_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CommandItem cmd)
            {
                cmd.UpgradeToBlocks();
                Save_Click(null, null);
            }
        }

        private void DeleteCommand_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CommandItem cmd)
            {
                CommandList.Remove(cmd);
                Save_Click(null, null);
            }
        }

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CommandItem cmd)
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
                var cmd = CommandList.FirstOrDefault(c => c.Actions.Contains(action));
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

        private void UpdateAvailableVariables()
        {
            AvailableVariables.Clear();
            if (ViewModel.Config?.GlobalVariables?.Scalars != null)
            {
                foreach (var pair in ViewModel.Config.GlobalVariables.Scalars)
                {
                    if (pair != null && !string.IsNullOrWhiteSpace(pair.Key))
                        AvailableVariables.Add(pair.Key);
                }
            }
            if (ViewModel.Config?.GlobalVariables?.Lists != null)
            {
                foreach (var pair in ViewModel.Config.GlobalVariables.Lists)
                {
                    if (pair != null && !string.IsNullOrWhiteSpace(pair.Key) && !AvailableVariables.Contains(pair.Key))
                        AvailableVariables.Add(pair.Key);
                }
            }
        }
    }
}
