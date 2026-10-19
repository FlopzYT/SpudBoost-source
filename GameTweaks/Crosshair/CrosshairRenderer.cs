using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GameTweaks.Core;

namespace GameTweaks.Crosshair;

/// <summary>Draws a crosshair onto a canvas; shared by the overlay and the live preview.</summary>
public static class CrosshairRenderer
{
    public static readonly string[] Styles = ["Cross", "T-shape", "X", "Dot", "Circle", "Circle + cross"];

    /// <summary>Draws <paramref name="s"/> centred on (cx, cy); <paramref name="extraGap"/> spreads it out (shooting bloom).</summary>
    public static void Draw(Canvas canvas, CrosshairLook s, double cx, double cy, double extraGap = 0)
    {
        canvas.Children.Clear();
        var color = ParseColor(s.Color);
        var fill = new SolidColorBrush(color);
        var outline = Brushes.Black;
        var gap = s.Gap + extraGap;

        var arms = s.Style is "Cross" or "T-shape" or "X" or "Circle + cross";
        if (arms)
        {
            var layer = new Canvas();
            if (s.Style == "X") layer.RenderTransform = new RotateTransform(45, cx, cy);
            var t = s.Thickness;
            // left, right, bottom, top (T-shape has no top arm)
            var rects = new List<Rect>
            {
                new(cx - gap - s.Size, cy - t / 2, s.Size, t),
                new(cx + gap, cy - t / 2, s.Size, t),
                new(cx - t / 2, cy + gap, t, s.Size),
            };
            if (s.Style != "T-shape") rects.Add(new Rect(cx - t / 2, cy - gap - s.Size, t, s.Size));

            if (s.Outline)
                foreach (var r in rects) AddRect(layer, Rect.Inflate(r, 1, 1), outline);
            foreach (var r in rects) AddRect(layer, r, fill);
            canvas.Children.Add(layer);
        }

        if (s.Style is "Circle" or "Circle + cross")
        {
            var d = (s.Size + gap) * 2;
            if (s.Outline) AddRing(canvas, cx, cy, d, s.Thickness + 2, outline);
            AddRing(canvas, cx, cy, d, s.Thickness, fill);
        }

        if (s.Style == "Dot" || s.CenterDot)
        {
            var d = s.Style == "Dot" ? Math.Max(2, s.Thickness * 2) : Math.Max(2, s.Thickness);
            if (s.Outline) AddDot(canvas, cx, cy, d + 2, outline);
            AddDot(canvas, cx, cy, d, fill);
        }

        canvas.Opacity = s.Opacity;
    }

    public static Color ParseColor(string text)
    {
        try { return (Color)ColorConverter.ConvertFromString(text); }
        catch (FormatException) { return Colors.Lime; }
    }

    static void AddRect(Canvas c, Rect r, Brush brush)
    {
        var shape = new Rectangle { Width = r.Width, Height = r.Height, Fill = brush, SnapsToDevicePixels = true };
        Canvas.SetLeft(shape, r.X);
        Canvas.SetTop(shape, r.Y);
        c.Children.Add(shape);
    }

    static void AddDot(Canvas c, double cx, double cy, double d, Brush brush)
    {
        var dot = new Ellipse { Width = d, Height = d, Fill = brush };
        Canvas.SetLeft(dot, cx - d / 2);
        Canvas.SetTop(dot, cy - d / 2);
        c.Children.Add(dot);
    }

    static void AddRing(Canvas c, double cx, double cy, double d, double thickness, Brush brush)
    {
        var ring = new Ellipse { Width = d, Height = d, Stroke = brush, StrokeThickness = thickness };
        Canvas.SetLeft(ring, cx - d / 2);
        Canvas.SetTop(ring, cy - d / 2);
        c.Children.Add(ring);
    }
}
