using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using HelixToolkit.Wpf.SharpDX;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private void DrawRulers()
    {
        if (RulerCanvas is null || Viewport.ActualWidth < 100 || Viewport.ActualHeight < 100) return;
        RulerCanvas.Children.Clear();
        if (Viewport.Camera is not HelixToolkit.Wpf.SharpDX.OrthographicCamera camera || Math.Abs(camera.LookDirection.X) + Math.Abs(camera.LookDirection.Y) > 0.01)
        { return; }
        var width = Viewport.ActualWidth; var height = Viewport.ActualHeight;
        var first = Viewport.UnProjectOnPlane(new Point(0, 0), new Point3D(), new Vector3D(0, 0, 1));
        var last = Viewport.UnProjectOnPlane(new Point(width, height), new Point3D(), new Vector3D(0, 0, 1));
        if (first is null || last is null) return;
        var span = Math.Abs(last.Value.X - first.Value.X);
        if (!double.IsFinite(span) || span <= 0) return;
        var desired = span / Math.Max(2, width / 80);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(desired)));
        var step = new[] { 1d, 2, 5, 10 }.First(f => f * magnitude >= desired) * magnitude;
        UpdateGrid((float)step);
        var background = new SolidColorBrush(Color.FromArgb(235, 27, 30, 36));
        var top = new Rectangle { Width = width, Height = 25, Fill = background }; RulerCanvas.Children.Add(top);
        var left = new Rectangle { Width = 40, Height = height, Fill = background }; RulerCanvas.Children.Add(left);
        var minX = Math.Min(first.Value.X, last.Value.X); var maxX = Math.Max(first.Value.X, last.Value.X);
        var minY = Math.Min(first.Value.Y, last.Value.Y); var maxY = Math.Max(first.Value.Y, last.Value.Y);
        for (var value = Math.Ceiling(minX / (step / 5)) * (step / 5); value <= maxX; value += step / 5)
        {
            var x = Viewport.Project(new Point3D(value, 0, 0)).X;
            var major = Math.Abs(value / step - Math.Round(value / step)) < 0.0001;
            Tick(x, 25, x, major ? 14 : 20); if (major && x > 40) Label($"{value:0.###}", x + 3, 1);
        }
        for (var value = Math.Ceiling(minY / (step / 5)) * (step / 5); value <= maxY; value += step / 5)
        {
            var y = Viewport.Project(new Point3D(0, value, 0)).Y;
            var major = Math.Abs(value / step - Math.Round(value / step)) < 0.0001;
            Tick(40, y, major ? 30 : 35, y); if (major && y > 25) Label($"{value:0.###}", 2, y + 2);
        }
        Label("mm", 3, 2);
        void Tick(double x1, double y1, double x2, double y2) => RulerCanvas.Children.Add(new Line
            { X1 = x1, X2 = x2, Y1 = y1, Y2 = y2, Stroke = Brushes.LightGray, StrokeThickness = 0.8 });
        void Label(string text, double x, double y)
        {
            var label = new TextBlock { Text = text, Foreground = Brushes.LightGray, FontSize = 11 };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); RulerCanvas.Children.Add(label);
        }
    }
}
