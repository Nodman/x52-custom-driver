using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using X52.CustomDriver.App.ViewModels;

namespace X52.CustomDriver.App
{
    /// <summary>
    /// Live top-down stick position, like a gamepad tester: a grey dot and line for the physical
    /// stick, a blue dot and line for the value sent to the game after the active profile's curves.
    /// A bar underneath does the same for the twist axis.
    /// </summary>
    public partial class StickPositionView : System.Windows.Controls.UserControl
    {
        private const double Size = 200;
        private const double TwistW = 200, TwistH = 14;

        private X52ViewModel? _vm;

        private readonly Line _rawLine = new() { Stroke = Brush("#666666"), StrokeThickness = 2 };
        private readonly Line _outLine = new() { Stroke = Brush("#00D2FF"), StrokeThickness = 2 };
        private readonly Ellipse _rawDot = new() { Width = 12, Height = 12, Fill = Brush("#888888") };
        private readonly Ellipse _outDot = new() { Width = 12, Height = 12, Fill = Brush("#00D2FF") };

        private readonly System.Windows.Shapes.Rectangle _twistRaw = new() { Height = TwistH / 2, Fill = Brush("#555555") };
        private readonly System.Windows.Shapes.Rectangle _twistOut = new() { Height = TwistH / 2, Fill = Brush("#00D2FF") };

        public StickPositionView()
        {
            InitializeComponent();
            DrawBackground();

            StickCanvas.Children.Add(_rawLine);
            StickCanvas.Children.Add(_outLine);
            StickCanvas.Children.Add(_rawDot);
            StickCanvas.Children.Add(_outDot);

            Canvas.SetTop(_twistRaw, 0);
            Canvas.SetTop(_twistOut, TwistH / 2);
            TwistCanvas.Children.Add(_twistRaw);
            TwistCanvas.Children.Add(_twistOut);
            TwistCanvas.Children.Add(new Line { X1 = TwistW / 2, X2 = TwistW / 2, Y1 = 0, Y2 = TwistH, Stroke = Brush("#444444"), StrokeThickness = 1 });

            Loaded += (s, e) => { if (_vm != null) { _vm.PropertyChanged -= Vm_PropertyChanged; _vm.PropertyChanged += Vm_PropertyChanged; Redraw(); } };
            Unloaded += (s, e) => { if (_vm != null) _vm.PropertyChanged -= Vm_PropertyChanged; };
            Redraw();
        }

        public void Initialize(X52ViewModel vm)
        {
            _vm = vm;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
            Redraw();
        }

        private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private void DrawBackground()
        {
            var faint = Brush("#1A1A1A");
            var mid = Brush("#2A2A2A");
            // Grid every 25%
            for (int i = 1; i < 4; i++)
            {
                double p = Size * i / 4.0;
                var b = i == 2 ? mid : faint;
                StickCanvas.Children.Add(new Line { X1 = p, Y1 = 0, X2 = p, Y2 = Size, Stroke = b, StrokeThickness = 1 });
                StickCanvas.Children.Add(new Line { X1 = 0, Y1 = p, X2 = Size, Y2 = p, Stroke = b, StrokeThickness = 1 });
            }
            // Circle for 50% and 100% deflection
            foreach (double r in new[] { 0.5, 1.0 })
            {
                var c = new Ellipse { Width = Size * r, Height = Size * r, Stroke = faint, StrokeThickness = 1 };
                Canvas.SetLeft(c, (Size - Size * r) / 2);
                Canvas.SetTop(c, (Size - Size * r) / 2);
                StickCanvas.Children.Add(c);
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(X52ViewModel.State) && IsVisible) Redraw();
        }

        // -1..1 → canvas pixels (stick forward = up)
        private static double Px(double v) => (v + 1.0) / 2.0 * Size;

        private void Redraw()
        {
            double rx = 0, ry = 0, rz = 0;
            double ox = 0, oy = 0, oz = 0;
            if (_vm != null)
            {
                rx = _vm.GetLiveInput("X");
                ry = _vm.GetLiveInput("Y");
                rz = _vm.GetLiveInput("Twist");
                var axes = _vm.CurrentProfile.AxisSettings;
                ox = axes.CurveX?.Apply(rx) ?? rx;
                oy = axes.CurveY?.Apply(ry) ?? ry;
                oz = axes.CurveTwist?.Apply(rz) ?? rz;
            }

            double c = Size / 2;
            PlaceVector(_rawLine, _rawDot, c, Px(rx), Px(ry));
            PlaceVector(_outLine, _outDot, c, Px(ox), Px(oy));

            PlaceTwist(_twistRaw, rz);
            PlaceTwist(_twistOut, oz);

            ValuesText.Text = $"X {rx * 100,4:0}% → {ox * 100,4:0}%   Y {ry * 100,4:0}% → {oy * 100,4:0}%   Twist {rz * 100,4:0}% → {oz * 100,4:0}%";
        }

        private static void PlaceVector(Line line, Ellipse dot, double c, double x, double y)
        {
            line.X1 = c; line.Y1 = c; line.X2 = x; line.Y2 = y;
            Canvas.SetLeft(dot, x - dot.Width / 2);
            Canvas.SetTop(dot, y - dot.Height / 2);
        }

        // Bar grows from the centre towards the twist direction
        private static void PlaceTwist(System.Windows.Shapes.Rectangle bar, double v)
        {
            double c = TwistW / 2;
            double end = c + v * c;
            Canvas.SetLeft(bar, System.Math.Min(c, end));
            bar.Width = System.Math.Max(1, System.Math.Abs(end - c));
        }
    }
}
