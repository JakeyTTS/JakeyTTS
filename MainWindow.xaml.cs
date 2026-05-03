using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JakeyTTS
{
    /// <summary>
    /// Ventana principal que gestiona la navegación, el tema y la consola de logs.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Instancia estática para acceso global desde servicios y otras páginas
        public static MainWindow Instance { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();
            Instance = this;

            // 1. Configuración visual avanzada
            SystemBackdrop = new MicaBackdrop(); // Efecto traslúcido de Windows 11
            ExtendsContentIntoTitleBar = true;   // Permite usar el espacio de la barra de título
            SetTitleBar(null);                   // El NavigationView gestionará el arrastre

            // 2. Aplicar el tema (Claro/Oscuro) al arrancar
            ApplySavedTheme();

            // 3. Navegación inicial a la Home (con el Header oculto)
            NavView.Header = null;
            ContentFrame.Navigate(typeof(HomePage));
        }

        /// <summary>
        /// Aplica el tema visual guardado en el archivo de configuración.
        /// </summary>
        private void ApplySavedTheme()
        {
            var savedTheme = TwitchService.Instance.Config.SelectedTheme;
            if (this.Content is FrameworkElement rootElement)
            {
                // 0 = Default, 1 = Light, 2 = Dark
                rootElement.RequestedTheme = (ElementTheme)savedTheme;
            }
        }

        /// <summary>
        /// Escribe un mensaje en la consola inferior de la aplicación de forma segura.
        /// </summary>
        public void Log(string message)
        {
            // Importante: Los eventos de Twitch vienen de hilos secundarios.
            // DispatcherQueue asegura que la UI se actualice en el hilo principal.
            this.DispatcherQueue.TryEnqueue(() =>
            {
                LogBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";

                // Desplazamiento automático al final del log
                LogScroll.ChangeView(0, LogScroll.ScrollableHeight, 1);
            });
        }

        #region Lógica de Navegación

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            // Caso: Click en el icono de configuración (Settings)
            if (args.IsSettingsInvoked)
            {
                sender.Header = "Settings";
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            // Casos: Menú lateral personalizado
            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item?.Tag == null) return;

            string tag = item.Tag.ToString();

            // GESTIÓN DEL HEADER:
            // Si vamos a la Home, ocultamos el título. Si no, usamos el nombre del botón.
            sender.Header = (tag == "Home") ? null : item.Content;

            switch (tag)
            {
                case "Home":
                    ContentFrame.Navigate(typeof(HomePage));
                    break;

                case "TTSConfig":
                    ContentFrame.Navigate(typeof(TTSConfigPage));
                    break;

                case "Commands":
                    ContentFrame.Navigate(typeof(CommandsPage));
                    break;

                case "Rewards":
                    ContentFrame.Navigate(typeof(RedeemPage));
                    break;
                case "History":
                    ContentFrame.Navigate(typeof(HistoryPage));
                    break;
            }
        }

        #endregion
    }
}