using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JakeyTTS
{
    public class PluginItem : BaseNotify
    {
        private string _id = "";
        public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }

        private string _name = "";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _version = "";
        public string Version { get => _version; set { _version = value; OnPropertyChanged(); } }

        private string _protocolVersion = "";
        public string ProtocolVersion { get => _protocolVersion; set { _protocolVersion = value; OnPropertyChanged(); } }

        private string _iconBase64 = "";
        public string IconBase64 { get => _iconBase64; set { _iconBase64 = value; OnPropertyChanged(); } }

        private List<string> _subscriptions = new();
        public List<string> Subscriptions { get => _subscriptions; set { _subscriptions = value; OnPropertyChanged(); } }

        private bool _isEnabled = false;
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    }
}