using JakeyTTS.Core;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;

namespace JakeyTTS.Views
{
    public sealed partial class DictionaryPage : Page
    {
        public ObservableCollection<PronunciationItem> DictionaryList { get; set; }

        public DictionaryPage()
        {
            this.InitializeComponent();
            var existing = TwitchService.Instance.Config.PronunciationDictionary ?? new List<PronunciationItem>();
            DictionaryList = new ObservableCollection<PronunciationItem>(existing);
        }

        private void AddRule_Click(object sender, RoutedEventArgs e)
        {
            DictionaryList.Add(new PronunciationItem
            {
                Word = "",
                Replacement = "",
                IsRegex = false,
                IsEnabled = true
            });
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PronunciationItem selected)
            {
                DictionaryList.Remove(selected);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TwitchService.Instance.Config.PronunciationDictionary = DictionaryList.ToList();
            TwitchService.Instance.Config.Save();
            MainWindow.Instance.Log("💾 Pronunciation Dictionary saved.");
        }
    }
}
