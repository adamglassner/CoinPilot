using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using CoinPilot.Services;

namespace CoinPilot.Views;

/// <summary>Small theme-aware charts drawn with WPF shapes: a line chart and a progress ring.</summary>
public static class Charts
{
    /// <summary>
    /// A line chart with a soft area fill, light gridlines and value labels. Redraws itself when resized.
    /// </summary>
    public static FrameworkElement LineChart(IReadOnlyList<(string Label, decimal Value)> points, double height = 200, string brushKey = "Brush.Accent")
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        System.Windows.Automation.AutomationProperties.SetName(root,
            "Chart: " + string.Join(", ", points.Select(p => $"{p.Label} {Money.Format(p.Value, cents: false)}")));

        var canvas = new Canvas { ClipToBounds = false, Margin = new Thickness(64, 8, 12, 0) };
        root.Children.Add(canvas);

        var labels = new UniformGrid { Rows = 1, Margin = new Thickness(64, 8, 12, 0) };
        Grid.SetRow(labels, 1);
        int every = Math.Max(1, (int)Math.Ceiling(points.Count / 12.0));
        for (int i = 0; i < points.Count; i++)
        {
            var t = Ui.Text(i % every == 0 || i == points.Count - 1 ? points[i].Label : "", muted: true, size: 11.5);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            labels.Children.Add(t);
        }
        root.Children.Add(labels);

        canvas.SizeChanged += (_, _) => Draw(canvas, points, brushKey);
        return root;
    }

    private static void Draw(Canvas canvas, IReadOnlyList<(string Label, decimal Value)> points, string brushKey)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w <= 0 || h <= 0 || points.Count == 0) return;

        decimal min = Math.Min(0, points.Min(p => p.Value)), max = Math.Max(0, points.Max(p => p.Value));
        if (max == min) max = min + 1;
        decimal pad = (max - min) * 0.08m;
        max += pad;
        if (min < 0) min -= pad;

        double X(int i) => points.Count == 1 ? w / 2 : w / points.Count * (i + 0.5);
        double Y(decimal v) => h - (double)((v - min) / (max - min)) * h;

        // Gridlines: 4 steps with labels on the left.
        for (int g = 0; g <= 4; g++)
        {
            decimal v = min + (max - min) * g / 4;
            double y = Y(v);
            var line = new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, StrokeThickness = 1 };
            line.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
            canvas.Children.Add(line);
            var label = Ui.Text(Money.Format(v, cents: false), muted: true, size: 11);
            label.TextAlignment = TextAlignment.Right;
            label.Width = 58;
            Canvas.SetLeft(label, -64);
            Canvas.SetTop(label, y - 8);
            canvas.Children.Add(label);
        }
        if (min < 0)
        {
            var zero = new Line { X1 = 0, X2 = w, Y1 = Y(0), Y2 = Y(0), StrokeThickness = 1.2, StrokeDashArray = [4, 3] };
            zero.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
            canvas.Children.Add(zero);
        }

        var pts = points.Select((p, i) => new Point(X(i), Y(p.Value))).ToList();

        // Area under the line
        var area = new Polygon { Opacity = 0.14 };
        area.Points.Add(new Point(pts[0].X, Y(Math.Max(min, 0))));
        foreach (var p in pts) area.Points.Add(p);
        area.Points.Add(new Point(pts[^1].X, Y(Math.Max(min, 0))));
        area.SetResourceReference(Shape.FillProperty, brushKey);
        canvas.Children.Add(area);

        var line2 = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        foreach (var p in pts) line2.Points.Add(p);
        line2.SetResourceReference(Shape.StrokeProperty, brushKey);
        canvas.Children.Add(line2);

        for (int i = 0; i < pts.Count; i++)
        {
            bool last = i == pts.Count - 1;
            var dot = new Ellipse { Width = last ? 10 : 7, Height = last ? 10 : 7, StrokeThickness = 2, ToolTip = $"{points[i].Label}: {Money.Format(points[i].Value)}" };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Surface");
            dot.SetResourceReference(Shape.StrokeProperty, brushKey);
            Canvas.SetLeft(dot, pts[i].X - dot.Width / 2);
            Canvas.SetTop(dot, pts[i].Y - dot.Height / 2);
            canvas.Children.Add(dot);
        }
    }

    /// <summary>A circular progress ring with the percentage in the middle.</summary>
    public static Grid Ring(double fraction, double size = 76, double thickness = 8, string brushKey = "Brush.Accent", string? centerText = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var grid = new Grid { Width = size, Height = size };
        double r = (size - thickness) / 2;
        var center = new Point(size / 2, size / 2);

        var track = new Ellipse { Width = size - thickness, Height = size - thickness, StrokeThickness = thickness, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        track.SetResourceReference(Shape.StrokeProperty, "Brush.Track");
        grid.Children.Add(track);

        if (fraction >= 0.999)
        {
            var full = new Ellipse { Width = size - thickness, Height = size - thickness, StrokeThickness = thickness, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            full.SetResourceReference(Shape.StrokeProperty, brushKey);
            grid.Children.Add(full);
        }
        else if (fraction > 0)
        {
            double angle = fraction * 360;
            var start = new Point(center.X, center.Y - r);
            double rad = (angle - 90) * Math.PI / 180;
            var end = new Point(center.X + r * Math.Cos(rad), center.Y + r * Math.Sin(rad));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true));
            var arc = new Path { Data = new PathGeometry([figure]), StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            arc.SetResourceReference(Shape.StrokeProperty, brushKey);
            grid.Children.Add(arc);
        }

        var text = Ui.Text(centerText ?? $"{fraction:P0}", size: size / 5.2, weight: FontWeights.Bold);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        grid.Children.Add(text);
        return grid;
    }
}
