using JakeyTTS.Core;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace JakeyTTS.Views
{
    public sealed partial class VariableManagerDialog : ContentDialog
    {
        public VariableStore Store { get; }

        public VariableManagerDialog(VariableStore store = null)
        {
            Store = store ?? TwitchService.Instance.Config.GlobalVariables;
            this.InitializeComponent();
            this.Title = store == null ? "Manage Global Variables" : "Manage Local Variables";
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            TwitchService.Instance.Config.Save();
            MainWindow.Instance?.Log("💾 Variables saved automatically.");
        }

        private void AddScalar_Click(object sender, RoutedEventArgs e)
        {
            Store.Scalars.Add(new StringPair { Key = "NewVar", Value = "0", IsNumber = false });
        }

        private void AddNumber_Click(object sender, RoutedEventArgs e)
        {
            Store.Scalars.Add(new StringPair { Key = "NewNum", Value = "0", IsNumber = true });
        }

        private void DeleteScalar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StringPair pair)
            {
                Store.Scalars.Remove(pair);
            }
        }

        private void AddList_Click(object sender, RoutedEventArgs e)
        {
            Store.Lists.Add(new StringListPair { Key = "NewList" });
        }

        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StringListPair pair)
            {
                pair.Values.Clear();
            }
        }

        private void DeleteList_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StringListPair pair)
            {
                Store.Lists.Remove(pair);
            }
        }

        private void AddItemToList_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var parentPanel = btn?.Parent as StackPanel;
            if (parentPanel != null)
            {
                var tb = parentPanel.Children.OfType<TextBox>().FirstOrDefault();
                var pair = btn.DataContext as StringListPair;
                if (tb != null && pair != null && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    pair.Values.Add(tb.Text);
                    tb.Text = "";
                }
            }
        }

        private void RemoveItemFromList_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.DataContext as string;
            
            FrameworkElement parent = btn;
            while (parent != null && !(parent.DataContext is StringListPair))
            {
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
            }

            if (parent?.DataContext is StringListPair pair && item != null)
            {
                pair.Values.Remove(item);
            }
        }
    }
}
