using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using X52.CustomDriver.App.ViewModels;

namespace X52.CustomDriver.App
{
    /// <summary>Live view of the stick-top 8-way hat: lit direction, a line from the centre, and the vJoy POV value.</summary>
    public partial class PovView : System.Windows.Controls.UserControl
    {
        private const double Size = 90, Radius = 34, DotSize = 12;
        private static readonly string[] Names = { "Up", "Up-Right", "Right", "Down-Right", "Down", "Down-Left", "Left", "Up-Left" };

        private X52ViewModel? _vm;
        private readonly Ellipse[] _dots = new Ellipse[8];
        private readonly Line _line = new() { Stroke = Brush("#00D2FF"), StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round };
        private readonly Ellipse _centre = new() { Width = DotSize, Height = DotSize };
        private int _shown = int.MinValue;

        private static readonly SolidColorBrush Idle = Brush("#262626");
        private static readonly SolidColorBrush Lit = Brush("#00D2FF");

        public PovView()
        {
            InitializeComponent();

            var ring = new Ellipse { Width = Radius * 2, Height = Radius * 2, Stroke = Brush("#1E1E1E"), StrokeThickness = 1 };
            Canvas.SetLeft(ring, Size / 2 - Radius);
            Canvas.SetTop(ring, Size / 2 - Radius);
            PovCanvas.Children.Add(ring);
            PovCanvas.Children.Add(_line);

            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4; // 0 = up, clockwise
                var dot = new Ellipse { Width = DotSize, Height = DotSize, Fill = Idle };
                Canvas.SetLeft(dot, Size / 2 + Math.Sin(a) * Radius - DotSize / 2);
                Canvas.SetTop(dot, Size / 2 - Math.Cos(a) * Radius - DotSize / 2);
                _dots[i] = dot;
                PovCanvas.Children.Add(dot);
            }
            Canvas.SetLeft(_centre, Size / 2 - DotSize / 2);
            Canvas.SetTop(_centre, Size / 2 - DotSize / 2);
            PovCanvas.Children.Add(_centre);

            Loaded += (s, e) => { if (_vm != null) { _vm.PropertyChanged -= Vm_PropertyChanged; _vm.PropertyChanged += Vm_PropertyChanged; } Show(_vm?.PovDirection ?? -1); };
            Unloaded += (s, e) => { if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged; };
            Show(-1);
        }

        public void Initialize(X52ViewModel vm)
        {
            _vm = vm;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
            Show(vm.PovDirection);
        }

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(X52ViewModel.State) && _vm != null && IsVisible) Show(_vm.PovDirection);
        }

        private void Show(int direction)
        {
            if (direction == _shown) return;
            _shown = direction;

            int index = direction < 0 ? -1 : (direction / 45) % 8;
            for (int i = 0; i < 8; i++) _dots[i].Fill = i == index ? Lit : Idle;
            _centre.Fill = index < 0 ? Lit : Idle;

            if (index < 0)
            {
                _line.Visibility = System.Windows.Visibility.Hidden;
                DirectionText.Text = "Centred";
                AngleText.Text = "vJoy POV 1: -1";
            }
            else
            {
                double a = index * Math.PI / 4;
                _line.X1 = Size / 2; _line.Y1 = Size / 2;
                _line.X2 = Size / 2 + Math.Sin(a) * (Radius - DotSize / 2);
                _line.Y2 = Size / 2 - Math.Cos(a) * (Radius - DotSize / 2);
                _line.Visibility = System.Windows.Visibility.Visible;
                DirectionText.Text = Names[index];
                AngleText.Text = $"vJoy POV 1: {direction}°";
            }
        }
    }
}
