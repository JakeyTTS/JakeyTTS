using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using JakeyTTS.Melodies;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace JakeyTTS
{
    public class DraggableDot : ContentControl
    {
        public DraggableDot()
        {
            this.Content = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.MediumOrchid),
                Margin = new Thickness(-10, -10, 0, 0)
            };
        }

        public void SetHandCursor(bool showHand)
        {
            this.ProtectedCursor = showHand
                ? Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand)
                : null;
        }
    }

    public sealed partial class MelodyEditorPage : Page
    {
        private Melody _melody;
        private ObservableCollection<MelodyPoint> _points;
        private MelodyPoint _draggingPoint;

        public MelodyEditorPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is Melody m)
            {
                _melody = m;
                NameInput.Text = _melody.Name;
                _points = new ObservableCollection<MelodyPoint>(_melody.Points.OrderBy(p => p.TimePct));
                _points.CollectionChanged += (s, args) => UpdateGraph();

                PointsList.ItemsSource = _points;
                VisualPointsItemsControl.ItemsSource = _points;

                DispatcherQueue.TryEnqueue(() => UpdateGraph());
            }
        }

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (_points == null || !_points.Any()) return;

            // Sincronizamos temporalmente los puntos actuales del editor con el objeto de la melodía
            // para que el motor de audio use el estado visual actual.
            var currentPoints = _points.OrderBy(p => p.TimePct).ToList();

            // Guardamos los puntos originales para no ensuciar la data si el usuario cancela
            var originalPoints = _melody.Points;
            _melody.Points = currentPoints;

            // Usamos el nombre del TextBox por si el usuario lo cambió
            string previewName = string.IsNullOrWhiteSpace(NameInput.Text) ? "preview" : NameInput.Text;
            string originalName = _melody.Name;
            _melody.Name = previewName;

            // Ejecutamos el test
            await TwitchService.Instance.ProcessAndSpeak($"[melody:{previewName}] This is a preview of your new melody sound.");

            // Restauramos los valores originales (el guardado real solo ocurre en Save_Click)
            _melody.Points = originalPoints;
            _melody.Name = originalName;
        }

        private void UpdateGraph()
        {
            if (EditorCanvas.ActualWidth == 0 || EditorCanvas.ActualHeight == 0) return;

            var sorted = _points.OrderBy(p => p.TimePct).ToList();
            var pCollection = new PointCollection();

            foreach (var p in sorted)
            {
                double x = p.TimePct * EditorCanvas.ActualWidth;
                double y = (2.0 - p.Pitch) / (2.0 - 0.5) * EditorCanvas.ActualHeight;

                pCollection.Add(new Point(x, y));

                var container = VisualPointsItemsControl.ContainerFromItem(p) as ContentPresenter;
                if (container != null)
                {
                    Canvas.SetLeft(container, x);
                    Canvas.SetTop(container, y);
                }
            }
            GraphLine.Points = pCollection;
        }

        private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(EditorCanvas).Position;
            float time = (float)(pt.X / EditorCanvas.ActualWidth);
            float pitch = (float)(2.0 - (pt.Y / EditorCanvas.ActualHeight * (2.0 - 0.5)));

            _points.Add(new MelodyPoint { TimePct = Math.Clamp(time, 0, 1), Pitch = Math.Clamp(pitch, 0.5f, 2.0f) });
        }

        private void Point_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            _draggingPoint = element.DataContext as MelodyPoint;
            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void Point_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingPoint != null)
            {
                var pt = e.GetCurrentPoint(EditorCanvas).Position;
                _draggingPoint.TimePct = Math.Clamp((float)(pt.X / EditorCanvas.ActualWidth), 0, 1);
                _draggingPoint.Pitch = Math.Clamp((float)(2.0 - (pt.Y / EditorCanvas.ActualHeight * 1.5)), 0.5f, 2.0f);
                UpdateGraph();
            }
        }

        private void Point_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _draggingPoint = null;
            (sender as FrameworkElement).ReleasePointerCapture(e.Pointer);
            PointsList.ItemsSource = null;
            PointsList.ItemsSource = _points;
        }

        private void Point_PointerEntered(object sender, PointerRoutedEventArgs e) => (sender as DraggableDot)?.SetHandCursor(true);
        private void Point_PointerExited(object sender, PointerRoutedEventArgs e) => (sender as DraggableDot)?.SetHandCursor(false);

        private void NumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateGraph();
        private void EditorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGraph();
        private void AddPoint_Click(object sender, RoutedEventArgs e) => _points.Add(new MelodyPoint { TimePct = 0.5f, Pitch = 1.0f });
        private void DeletePoint_Click(object sender, RoutedEventArgs e) { if ((sender as Button).Tag is MelodyPoint p) _points.Remove(p); }
        private void Back_Click(object sender, RoutedEventArgs e) => this.Frame.GoBack();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameInput.Text)) return;
            _melody.Name = NameInput.Text;
            _melody.Points = _points.OrderBy(p => p.TimePct).ToList();
            MelodyService.Instance.Save(_melody);
            this.Frame.GoBack();
        }
    }
}