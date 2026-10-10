using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

internal enum Corner
{
	TopLeft,
	TopRight,
	BottomRight,
	BottomLeft,
}

/// <summary>The element selected on the canvas, plus the selection box and corner handles drawn for it.</summary>
internal sealed class SelectionLayer
{
	private static readonly Corner[] AllCorners = { Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft };

	public CanvasElement? SelectedElement { get; set; }

	/// <summary>Drops the selection when the document no longer contains it (e.g. a new background was loaded).</summary>
	public void SyncWithDocument(Document? document)
	{
		if (SelectedElement is not null && document is not null && !document.Elements.Contains(SelectedElement))
			SelectedElement = null;
	}

	public Corner? HitTestHandle(Point position, Rect imageRect, double scale)
	{
		var element = SelectedElement;
		if (element is null)
			return null;

		var bounds = element.Bounds;
		foreach (var corner in AllCorners)
		{
			var screen = ToScreen(GetCorner(bounds, corner), imageRect, scale);
			if (Math.Abs(position.X - screen.X) <= ThemeDefinition.HandleHitRadius &&
				Math.Abs(position.Y - screen.Y) <= ThemeDefinition.HandleHitRadius)
				return corner;
		}

		return null;
	}

	/// <summary>The corner opposite <paramref name="corner"/>, which stays fixed while scaling.</summary>
	public static SKPoint GetAnchor(SKRect bounds, Corner corner)
	{
		return corner switch
		{
			Corner.TopLeft => new SKPoint(bounds.Right, bounds.Bottom),
			Corner.TopRight => new SKPoint(bounds.Left, bounds.Bottom),
			Corner.BottomRight => new SKPoint(bounds.Left, bounds.Top),
			_ => new SKPoint(bounds.Right, bounds.Top),
		};
	}

	public static SKPoint GetCorner(SKRect bounds, Corner corner)
	{
		return corner switch
		{
			Corner.TopLeft => new SKPoint(bounds.Left, bounds.Top),
			Corner.TopRight => new SKPoint(bounds.Right, bounds.Top),
			Corner.BottomRight => new SKPoint(bounds.Right, bounds.Bottom),
			_ => new SKPoint(bounds.Left, bounds.Bottom),
		};
	}

	public void Draw(DrawingContext context, Rect imageRect, double scale)
	{
		if (SelectedElement is null)
			return;

		var bounds = SelectedElement.Bounds;
		var topLeft = ToScreen(new SKPoint(bounds.Left, bounds.Top), imageRect, scale);
		var bottomRight = ToScreen(new SKPoint(bounds.Right, bounds.Bottom), imageRect, scale);
		context.DrawRectangle(ThemeDefinition.SelectionPen, new Rect(topLeft, bottomRight));

		var half = ThemeDefinition.HandleSize / 2;
		foreach (var corner in AllCorners)
		{
			var screen = ToScreen(GetCorner(bounds, corner), imageRect, scale);
			var handle = new Rect(screen.X - half, screen.Y - half, ThemeDefinition.HandleSize, ThemeDefinition.HandleSize);
			context.FillRectangle(ThemeDefinition.HandleFill, handle);
			context.DrawRectangle(ThemeDefinition.SelectionPen, handle);
		}
	}

	private static Point ToScreen(SKPoint point, Rect imageRect, double scale) => new Point(imageRect.X + point.X * scale, imageRect.Y + point.Y * scale);
}
