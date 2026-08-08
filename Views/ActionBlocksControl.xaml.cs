using JakeyTTS.Core;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JakeyTTS.Views
{
    public sealed partial class ActionBlocksControl : UserControl
    {
        public TwitchService ViewModel => TwitchService.Instance;

        public List<CommandCondition> Conditions { get; } = Enum.GetValues(typeof(CommandCondition)).Cast<CommandCondition>().ToList();
        
        public ObservableCollection<TriggerOption> AvailableTriggers { get; } = new();

        public ActionBlocksControl()
        {
            this.InitializeComponent();

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

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is IActionableItem cmd)
            {
                cmd.Actions.Add(new CommandAction { ParentItem = cmd });
                ViewModel.Config.Save();
            }
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.DataContext is CommandAction action && this.DataContext is IActionableItem cmd)
            {
                cmd.Actions.Remove(action);
                ViewModel.Config.Save();
            }
        }
    }
}
