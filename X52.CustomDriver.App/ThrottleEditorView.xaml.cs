using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using X52.CustomDriver.App.ViewModels;
using X52.CustomDriver.Core.Models;

namespace X52.CustomDriver.App
{
    /// <summary>
    /// Throttle deadzone editor, the throttle's counterpart of <see cref="CurveEditorView"/>:
    /// a graph of physical throttle (x) against what the game gets (y), with the end deadzones
    /// and the centre band shaded, and a live dot for the throttle right now.
    /// </summary>
    public partial class ThrottleEditorView : System.Windows.Controls.UserControl
    {
        private const double Size = 320;

        private X52ViewModel? _vm;
        private X52Profile? _profile;
        private bool _loading;

        private readonly System.Windows.Shapes.Rectangle _lowerZone = Zone("#33FF8800", "#88FF8800");
        private readonly System.Windows.Shapes.Rectangle _upperZone = Zone("#33FF8800", "#88FF8800");
        private readonly System.Windows.Shapes.Rectangle _centerZone = Zone("#3300D2FF", "#8800D2FF");
        private readonly Line _centerMark = new() { Stroke = Brush("#5500D2FF"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
        private readonly Polyline _line = new() { Stroke = Brush("#00D2FF"), StrokeThickness = 2 };
        private readonly Line _liveMark = new() { Stroke = Brush("#55FF0055"), StrokeThickness = 1 };
        private readonly Ellipse _dot = new() { Width = 12, Height = 12, Fill = Brush("#FF0055") };

        public ThrottleEditorView()
        {
            InitializeComponent();
            // Zones under the grid lines, line and dot on top
            GraphCanvas.Children.Add(_lowerZone);
            GraphCanvas.Children.Add(_upperZone);
            GraphCanvas.Children.Add(_centerZone);
            DrawGrid();
            GraphCanvas.Children.Add(_centerMark);
            GraphCanvas.Children.Add(_liveMark);
            GraphCanvas.Children.Add(_line);
            GraphCanvas.Children.Add(_dot);
            Unloaded += (s, e) => { if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged; };
            Loaded += (s, e) => { if (_vm != null) { _vm.PropertyChanged -= Vm_PropertyChanged; _vm.PropertyChanged += Vm_PropertyChanged; } };
        }

        public void Initialize(X52ViewModel vm)
        {
            _vm = vm;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        /// <summary>Show and edit the throttle settings of this profile.</summary>
        public void SetProfile(X52Profile? profile)
        {
            _profile = profile;
            IsEnabled = profile != null;
            if (profile != null) Load();
        }

        private ThrottleSettings Settings => _profile!.AxisSettings.Throttle ??= new ThrottleSettings();

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private static System.Windows.Shapes.Rectangle Zone(string fill, string stroke) =>
            new() { Fill = Brush(fill), Stroke = Brush(stroke), StrokeThickness = 0, Height = Size };

        // --- Graph ---

        // Position 0..1 → pixels (output 1 = top)
        private static double ToX(double t) => t * Size;
        private static double ToY(double u) => (1.0 - u) * Size;

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
            // Linear reference (no deadzones)
            GraphCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = Size, X2 = Size, Y2 = 0,
                Stroke = Brush("#333333"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 }
            });
        }

        private void Redraw()
        {
            if (_profile == null) return;
            var t = Settings;
            var (lo, hi, hasCenter, cLo, cHi) = t.Zones();

            PlaceZone(_lowerZone, 0, lo);
            PlaceZone(_upperZone, hi, 1);
            _centerZone.Visibility = _centerMark.Visibility = hasCenter ? Visibility.Visible : Visibility.Collapsed;
            if (hasCenter)
            {
                PlaceZone(_centerZone, cLo, cHi);
                double cx = ToX((cLo + cHi) / 2);
                _centerMark.X1 = _centerMark.X2 = cx; _centerMark.Y1 = 0; _centerMark.Y2 = Size;
            }

            var points = new PointCollection();
            for (int i = 0; i <= 320; i++)
            {
                double x = i / 320.0;
                points.Add(new System.Windows.Point(ToX(x), ToY(t.Apply(x))));
            }
            _line.Points = points;
            UpdateDot();
        }

        private static void PlaceZone(System.Windows.Shapes.Rectangle r, double from, double to)
        {
            Canvas.SetLeft(r, ToX(from));
            Canvas.SetTop(r, 0);
            r.Width = Math.Max(0, ToX(to) - ToX(from));
            r.Visibility = r.Width > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateDot()
        {
            if (_vm == null || _profile == null) return;
            double input = _vm.ThrottleInput;
            double output = Settings.Apply(input);
            double x = ToX(input), y = ToY(output);
            _liveMark.X1 = _liveMark.X2 = x; _liveMark.Y1 = 0; _liveMark.Y2 = Size;
            Canvas.SetLeft(_dot, x - _dot.Width / 2);
            Canvas.SetTop(_dot, y - _dot.Height / 2);
            LiveText.Text = $"in {input * 100,4:0}%  →  out {output * 100,4:0}%";
        }

        // --- Data ---

        private void Load()
        {
            if (_profile == null) return;
            _loading = true;
            var t = Settings;
            LowerSlider.Value = t.LowerDeadzone * 100;
            UpperSlider.Value = t.UpperDeadzone * 100;
            CenterSlider.Value = t.CenterDeadzone * 100;
            CenterPosSlider.Value = t.CenterPosition * 100;
            InvertBox.IsChecked = t.Invert;
            _loading = false;
            UpdateCenterControls();
            Redraw();
        }

        private void ApplyFromUi()
        {
            if (_profile == null || _loading) return;
            var t = Settings;
            t.LowerDeadzone = LowerSlider.Value / 100.0;
            t.UpperDeadzone = UpperSlider.Value / 100.0;
            t.CenterDeadzone = CenterSlider.Value / 100.0;
            t.CenterPosition = CenterPosSlider.Value / 100.0;
            t.Invert = InvertBox.IsChecked == true;
            UpdateCenterControls();
            Redraw();
        }

        // Centre position only matters while there is a centre band
        private void UpdateCenterControls()
        {
            bool on = CenterSlider.Value > 0;
            CenterPositionRow.IsEnabled = CenterButtonsRow.IsEnabled = on;
            CenterPositionRow.Opacity = CenterButtonsRow.Opacity = on ? 1.0 : 0.4;
        }

        // --- Events ---

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(X52ViewModel.State) && IsVisible) UpdateDot();
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyFromUi();

        private void Invert_Changed(object sender, RoutedEventArgs e) => ApplyFromUi();

        private void SetCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || _profile == null) return;
            // Whole percent, like the slider; the slider change applies it and bubbles up as an edit
            CenterPosSlider.Value = Math.Clamp(Math.Round(_vm.ThrottleInput * 100), CenterPosSlider.Minimum, CenterPosSlider.Maximum);
        }

        private void CenterDefault_Click(object sender, RoutedEventArgs e) => CenterPosSlider.Value = 50;

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_profile == null) return;
            Settings.Reset();
            Load(); // slider changes bubble up to the profile view, which marks the profile as edited
            ApplyFromUi();
        }
    }
}
