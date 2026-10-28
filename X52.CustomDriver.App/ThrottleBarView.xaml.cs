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
    /// LIVE throttle bar: the physical throttle (grey, top half) and the value sent to the game
    /// after the active profile's deadzones (pink, bottom half), with the deadzones shaded.
    /// </summary>
    public partial class ThrottleBarView : System.Windows.Controls.UserControl
    {
        private const double H = 22;
        private X52ViewModel? _vm;

        private readonly System.Windows.Shapes.Rectangle _lowerZone = Zone("#33FF8800");
        private readonly System.Windows.Shapes.Rectangle _upperZone = Zone("#33FF8800");
        private readonly System.Windows.Shapes.Rectangle _centerZone = Zone("#3300D2FF");
        private readonly Line _centerMark = new() { Stroke = Brush("#8800D2FF"), StrokeThickness = 1, Y1 = 0, Y2 = H };
        private readonly System.Windows.Shapes.Rectangle _raw = new() { Height = H / 2 - 1, Fill = Brush("#666666") };
        private readonly System.Windows.Shapes.Rectangle _out = new() { Height = H / 2 - 1, Fill = Brush("#FF0055") };

        public ThrottleBarView()
        {
            InitializeComponent();
            BarCanvas.Children.Add(_lowerZone);
            BarCanvas.Children.Add(_upperZone);
            BarCanvas.Children.Add(_centerZone);
            Canvas.SetTop(_raw, 0);
            Canvas.SetTop(_out, H / 2 + 1);
            BarCanvas.Children.Add(_raw);
            BarCanvas.Children.Add(_out);
            BarCanvas.Children.Add(_centerMark);
            Loaded += (s, e) => { if (_vm != null) { _vm.PropertyChanged -= Vm_PropertyChanged; _vm.PropertyChanged += Vm_PropertyChanged; Redraw(); } };
            Unloaded += (s, e) => { if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged; };
        }

        public void Initialize(X52ViewModel vm)
        {
            _vm = vm;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
            Redraw();
        }

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        private static System.Windows.Shapes.Rectangle Zone(string fill) => new() { Fill = Brush(fill), Height = H };

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if ((e.PropertyName == nameof(X52ViewModel.State) || e.PropertyName == nameof(X52ViewModel.CurrentProfile)) && IsVisible) Redraw();
        }

        private void BarCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        private void Redraw()
        {
            double w = BarCanvas.ActualWidth;
            if (w <= 0) return;

            double input = 0, output = 0;
            var t = _vm?.CurrentProfile.AxisSettings.Throttle ?? new ThrottleSettings();
            if (_vm != null)
            {
                input = _vm.ThrottleInput;
                output = t.Apply(input);
            }

            var (lo, hi, hasCenter, cLo, cHi) = t.Zones();
            Place(_lowerZone, 0, lo * w);
            Place(_upperZone, hi * w, w);
            _centerZone.Visibility = _centerMark.Visibility = hasCenter ? Visibility.Visible : Visibility.Collapsed;
            if (hasCenter)
            {
                Place(_centerZone, cLo * w, cHi * w);
                _centerMark.X1 = _centerMark.X2 = (cLo + cHi) / 2 * w;
            }

            Place(_raw, 0, input * w);
            Place(_out, 0, output * w);
            ValuesText.Text = $"{input * 100,4:0}% → {output * 100,4:0}%";
        }

        private static void Place(System.Windows.Shapes.Rectangle r, double from, double to)
        {
            Canvas.SetLeft(r, from);
            r.Width = Math.Max(0, to - from);
            r.Visibility = r.Width > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
