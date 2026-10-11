using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using WhatSchedule.Documents;

namespace WhatSchedule.Controls;

/// <summary>
/// Creates, picks and draws the user-placed aligning lines stored on the
/// document. View-only behaviour in document coordinates, so guides are
/// never rendered into exported images.
/// </summary>
internal static class GuideLayer
{
	public static Guide? CreateFromRuler(Point position, Document document, Rect imageRect, double scale)
	{
		var onHorizontalRuler = position.X >= ThemeDefinition.RulerThickness && position.Y < ThemeDefinition.RulerThickness;
		var onVerticalRuler = position.X < ThemeDefinition.RulerThickness && position.Y >= ThemeDefinition.RulerThickness;
		if (!onHorizontalRuler && !onVerticalRuler)
			return null;

		var point = ScreenToDocument(position, imageRect, scale);
		var guide = new Guide
		{
			IsHorizontal = onHorizontalRuler,
			Position = onHorizontalRuler
				? Math.Clamp(point.Y, 0, document.Height)
				: Math.Clamp(point.X, 0, document.Width),
		};
		document.AddGuide(guide);
		return guide;
	}

	public static Guide? HitTest(Point position, Document document, Rect imageRect, double scale)
	{
		var point = ScreenToDocument(position, imageRect, scale);
		var tolerance = (float)(ThemeDefinition.GuideHitTolerance / scale);
		for (var i = document.Guides.Count - 1; i >= 0; i--)
		{
			var guide = document.Guides[i];
			if (guide.IsHorizontal ? Math.Abs(guide.Position - point.Y) <= tolerance : Math.Abs(guide.Position - point.X) <= tolerance)
				return guide;
		}

		return null;
	}

	public static void Move(Guide guide, Point position, Document document, Rect imageRect, double scale)
	{
		var point = ScreenToDocument(position, imageRect, scale);
		guide.Position = guide.IsHorizontal ? Math.Clamp(point.Y, 0, document.Height) : Math.Clamp(point.X, 0, document.Width);
	}

	/// <summary>True when <paramref name="position"/> is back inside the ruler strip the guide came from, meaning the guide should be removed.</summary>
	public static bool IsOverRuler(Guide guide, Point position)
	{
		return guide.IsHorizontal ? position.Y < ThemeDefinition.RulerThickness : position.X < ThemeDefinition.RulerThickness;
	}

	public static void Draw(DrawingContext context, Rect content, Rect imageRect, double scale, Document document)
	{
		if (document.Guides.Count == 0)
			return;

		using var clipState = context.PushClip(content);
		foreach (var guide in document.Guides)
		{
			if (guide.IsHorizontal)
			{
				var y = imageRect.Y + guide.Position * scale;
				context.DrawLine(ThemeDefinition.GuidePen, new Point(content.X, y), new Point(content.Right, y));
			}
			else
			{
				var x = imageRect.X + guide.Position * scale;
				context.DrawLine(ThemeDefinition.GuidePen, new Point(x, content.Y), new Point(x, content.Bottom));
			}
		}
	}

	private static SKPoint ScreenToDocument(Point position, Rect imageRect, double scale)
	{
		return new SKPoint(
			(float)((position.X - imageRect.X) / scale),
			(float)((position.Y - imageRect.Y) / scale));
	}
}
