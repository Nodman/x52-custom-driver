using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using X52.CustomDriver.App.ViewModels;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.App
{
    public partial class CurveEditorWindow : Window
    {
        private const double Size = 320;

        private readonly X52ViewModel? _vm;
        private string _axis = "X";
        private bool _loading;

        private readonly Polyline _curveLine = new() { Stroke = Brush("#00D2FF"), StrokeThickness = 2 };
        private readonly Ellipse _dot = new() { Width = 12, Height = 12, Fill = Brush("#FF0055") };
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

        public CurveEditorWindow(X52ViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            DrawGrid();
            GraphCanvas.Children.Add(_curveLine);
            GraphCanvas.Children.Add(_dot);

            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _vm.SaveProfiles(); };
            _vm.PropertyChanged += Vm_PropertyChanged;
            Closed += (s, e) =>
            {
                _vm.PropertyChanged -= Vm_PropertyChanged;
                if (_saveTimer.IsEnabled) { _saveTimer.Stop(); _vm.SaveProfiles(); }
            };

            LoadAxis();
        }

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        // --- Graph ---

        private static double ToX(double input) => (input + 1.0) / 2.0 * Size;
        private static double ToY(double output) => (1.0 - output) / 2.0 * Size;

        private void DrawGrid()
        {
            var grid = Brush("#1E1E1E");
            for (int i = 1; i < 8; i++)
            {
                double p = Size * i / 8.0;
                var brush = i == 4 ? Brush("#333333") : grid;
                GraphCanvas.Children.Add(new Line { X1 = p, Y1 = 0, X2 = p, Y2 = Size, Stroke = brush, StrokeThickness = 1 });
                GraphCanvas.Children.Add(new Line { X1 = 0, Y1 = p, X2 = Size, Y2 = p, Stroke = brush, StrokeThickness = 1 });
            }
            // Linear reference
            GraphCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = Size, X2 = Size, Y2 = 0,
                Stroke = Brush("#333333"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 }
            });
        }

        private void RedrawCurve()
        {
            if (_vm == null) return;
            var curve = _vm.GetCurve(_axis);
            var points = new PointCollection();
            for (int i = 0; i <= 200; i++)
            {
                double t = -1.0 + i / 100.0;
                points.Add(new System.Windows.Point(ToX(t), ToY(curve.Apply(t))));
            }
            _curveLine.Points = points;
            UpdateDot();
        }

        private void UpdateDot()
        {
            if (_vm == null) return;
            double input = _vm.GetLiveInput(_axis);
            double output = _vm.GetCurve(_axis).Apply(input);
            Canvas.SetLeft(_dot, ToX(input) - _dot.Width / 2);
            Canvas.SetTop(_dot, ToY(output) - _dot.Height / 2);
            LiveText.Text = $"in {input * 100,4:0}%  →  out {output * 100,4:0}%";
        }

        // --- Data ---

        private void LoadAxis()
        {
            if (_vm == null) return;
            _loading = true;
            var c = _vm.GetCurve(_axis);
            DeadzoneSlider.Value = c.Deadzone * 100;
            CurvatureSlider.Value = c.Curvature * 100;
            SatXSlider.Value = c.SaturationX * 100;
            SatYSlider.Value = c.SaturationY * 100;
            InvertBox.IsChecked = c.Invert;
            _loading = false;

            ProfileText.Text = $"PROFILE: {_vm.ProfileName}";
            foreach (var b in new[] { AxisXButton, AxisYButton, AxisTwistButton })
            {
                bool selected = (string)b.Tag == _axis;
                b.Background = selected ? Brush("#00D2FF") : Brush("#222");
                b.Foreground = selected ? System.Windows.Media.Brushes.Black : Brush("#00D2FF");
            }
            RedrawCurve();
        }

        private void ApplyFromUi()
        {
            if (_vm == null || _loading) return;
            var c = _vm.GetCurve(_axis);
            c.Deadzone = DeadzoneSlider.Value / 100.0;
            c.Curvature = CurvatureSlider.Value / 100.0;
            c.SaturationX = SatXSlider.Value / 100.0;
            c.SaturationY = SatYSlider.Value / 100.0;
            c.Invert = InvertBox.IsChecked == true;
            RedrawCurve();
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // --- Events ---

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(X52ViewModel.State)) UpdateDot();
            else if (e.PropertyName == nameof(X52ViewModel.CurrentProfile)) LoadAxis();
        }

        private void Axis_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.Tag is string axis)
            {
                _axis = axis;
                LoadAxis();
            }
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyFromUi();

        private void Invert_Changed(object sender, RoutedEventArgs e) => ApplyFromUi();

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            _vm.GetCurve(_axis).Reset();
            LoadAxis();
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }
}
