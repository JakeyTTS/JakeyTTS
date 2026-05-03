using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace JakeyTTS
{
    public sealed partial class CommandsPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;
        public ObservableCollection<CommandItem> CommandList { get; set; }

        public CommandsPage()
        {
            this.InitializeComponent();
            var existing = _service.Config.Commands ?? new System.Collections.Generic.List<CommandItem>();
            CommandList = new ObservableCollection<CommandItem>(existing);
            CommandsGrid.ItemsSource = CommandList;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            CommandList.Add(new CommandItem { Trigger = "!new", Response = "Hello {user}!", IsEnabled = true });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (CommandsGrid.SelectedItem is CommandItem selected)
            {
                CommandList.Remove(selected);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _service.Config.Commands = CommandList.ToList();
            _service.Config.Save();
            MainWindow.Instance?.Log("💾 Commands saved successfully.");
        }
    }
}