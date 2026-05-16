using JakeyTTS.Twitch;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace JakeyTTS
{
    public sealed partial class CommandsPage : Page
    {
        public TwitchService ViewModel => TwitchService.Instance;
        public ObservableCollection<CommandItem> CommandList { get; set; }

        public CommandsPage()
        {
            this.InitializeComponent();
            var existing = ViewModel.Config.Commands ?? new System.Collections.Generic.List<CommandItem>();
            CommandList = new ObservableCollection<CommandItem>(existing);

            CommandsTable.ItemsSource = CommandList;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            CommandList.Add(new CommandItem { Trigger = "!new", Response = "Hello {user}!", IsEnabled = true });
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