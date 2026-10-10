using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using WhatSchedule.Documents;

namespace WhatSchedule.Controls;

/// <summary>
/// User-placed aligning lines over the preview. View-only state in document
/// coordinates, so guides are never rendered into exported images.
/// </summary>
internal sealed class GuideLayer
{
	private readonly List<GuideLine> guides = new();

	public void Clear() => guides.Clear();

	public void Remove(GuideLine guide) => guides.Remove(guide);

	public GuideLine? CreateFromRuler(Point position, Document document, Rect imageRect, double scale)
	{
		var onHorizontalRuler = position.X >= ThemeDefinition.RulerThickness && position.Y < ThemeDefinition.RulerThickness;
		var onVerticalRuler = position.X < ThemeDefinition.RulerThickness && position.Y >= ThemeDefinition.RulerThickness;
		if (!onHorizontalRuler && !onVerticalRuler)
			return null;

		var point = ScreenToDocument(position, imageRect, scale);
		var guide = onHorizontalRuler
			? new GuideLine(isHorizontal: true, Math.Clamp(point.Y, 0, document.Height))
			: new GuideLine(isHorizontal: false, Math.Clamp(point.X, 0, document.Width));
		guides.Add(guide);
		return guide;
	}

	public GuideLine? HitTest(Point position, Rect imageRect, double scale)
	{
		var point = ScreenToDocument(position, imageRect, scale);
		var tolerance = (float)(ThemeDefinition.GuideHitTolerance / scale);
		for (var i = guides.Count - 1; i >= 0; i--)
		{
			var guide = guides[i];
			if (guide.IsHorizontal ? Math.Abs(guide.Position - point.Y) <= tolerance : Math.Abs(guide.Position - point.X) <= tolerance)
				return guide;
		}

		return null;
	}

	public static void Move(GuideLine guide, Point position, Document document, Rect imageRect, double scale)
	{
		var point = ScreenToDocument(position, imageRect, scale);
		guide.Position = guide.IsHorizontal
			? Math.Clamp(point.Y, 0, document.Height)
			: Math.Clamp(point.X, 0, document.Width);
	}

	/// <summary>True when <paramref name="position"/> is back inside the ruler strip the guide came from, meaning the guide should be removed.</summary>
	public static bool IsOverRuler(GuideLine guide, Point position)
	{
		return guide.IsHorizontal ? position.Y < ThemeDefinition.RulerThickness : position.X < ThemeDefinition.RulerThickness;
	}

	public void Draw(DrawingContext context, Rect content, Rect imageRect, double scale)
	{
		if (guides.Count == 0)
			return;

		using var clipState = context.PushClip(content);
		foreach (var guide in guides)
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

	private static SKPoint ScreenToDocument(Point position, Rect imageRect, double scale) => new(
		(float)((position.X - imageRect.X) / scale),
		(float)((position.Y - imageRect.Y) / scale));
}

/// <summary>A user-placed aligning line over the preview, stored in document coordinates.</summary>
internal sealed class GuideLine
{
	public GuideLine(bool isHorizontal, float position)
	{
		IsHorizontal = isHorizontal;
		Position = position;
	}

	/// <summary>True for a line drawn across the document width, positioned on the Y axis.</summary>
	public bool IsHorizontal { get; }

	/// <summary>The document-pixel coordinate on the axis the line is positioned on.</summary>
	public float Position { get; set; }
}
