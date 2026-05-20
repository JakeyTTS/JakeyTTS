using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;

namespace JakeyTTS.Plugins
{
    public partial class PluginDocsPage : Page
    {
        private int _currentPageIndex = 0;
        private const int MaxPages = 7;

        private readonly Dictionary<string, string> _sectionMapping = new Dictionary<string, string>
        {
            { "1. Infrastructure Server", "SecServer" },
            { "2. Registration Handshake", "SecHandshake" },
            { "3. Core TTS Execution", "SecTTS" },
            { "4. Global Variable Injection", "SecVariables" },
            { "5. Capability Injections", "SecInjections" },
            { "6. Remote Web UI Ingestion", "SecUI" },
            { "7. Custom Trigger Plugins", "SecTriggers" }
        };

        private readonly string[] _panelNames = { "SecServer", "SecHandshake", "SecTTS", "SecVariables", "SecInjections", "SecUI", "SecTriggers" };

        public PluginDocsPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) => SyncWizardState();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack) this.Frame.GoBack();
        }

        private void DocsTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && node.Content != null)
            {
                string contentString = node.Content.ToString();

                if (_sectionMapping.TryGetValue(contentString, out string targetSectionName))
                {
                    int index = Array.IndexOf(_panelNames, targetSectionName);
                    if (index >= 0)
                    {
                        _currentPageIndex = index;
                        SyncWizardState();
                    }
                }
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                SyncWizardState();
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex < MaxPages - 1)
            {
                _currentPageIndex++;
                SyncWizardState();
            }
        }

        private void SyncWizardState()
        {
            PrevBtn.IsEnabled = _currentPageIndex > 0;
            NextBtn.IsEnabled = _currentPageIndex < MaxPages - 1;

            string targetedPanelName = _panelNames[_currentPageIndex];

            foreach (var panelName in _panelNames)
            {
                var panel = this.FindName(panelName) as FrameworkElement;
                if (panel != null)
                {
                    panel.Visibility = (panelName == targetedPanelName) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            if (DocsTree.RootNodes.Count > _currentPageIndex)
            {
                var targetNode = DocsTree.RootNodes[_currentPageIndex];
                if (targetNode != null && DocsTree.SelectedNode != targetNode)
                {
                    DocsTree.SelectedNode = targetNode;
                }
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string targetName)
            {
                var textBlock = this.FindName(targetName) as TextBlock;
                if (textBlock == null) return;

                string content = textBlock.Text;

                if (string.IsNullOrEmpty(content) && textBlock.Inlines.Count > 0)
                {
                    content = string.Join("", textBlock.Inlines.Select(i =>
                        i is Run r ? r.Text : (i is LineBreak ? "\n" : "")));
                }

                if (string.IsNullOrWhiteSpace(content)) return;

                var dataPackage = new DataPackage();
                dataPackage.SetText(content);
                Clipboard.SetContent(dataPackage);

                MainWindow.Instance?.Log($"📋 Copied JSON template payload data safely to system clipboard memory channels.");
            }
        }
    }
}