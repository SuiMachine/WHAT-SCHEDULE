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

/// <summary>The element selected on the canvas, plus the rotated selection box, the corner handles and the rotation handle drawn for it.</summary>
internal sealed class SelectionLayer
{
	private static readonly Corner[] AllCorners = { Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft };

	private CanvasElement? selectedElement;

	/// <summary>Raised after the selected element changed.</summary>
	public event Action? SelectionChanged;

	public CanvasElement? SelectedElement
	{
		get => selectedElement;
		set
		{
			if (ReferenceEquals(selectedElement, value))
				return;

			selectedElement = value;
			SelectionChanged?.Invoke();
		}
	}

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
			var screen = ToScreen(element.ToDocument(GetCorner(bounds, corner)), imageRect, scale);
			if (Math.Abs(position.X - screen.X) <= ThemeDefinition.HandleHitRadius &&
				Math.Abs(position.Y - screen.Y) <= ThemeDefinition.HandleHitRadius)
				return corner;
		}

		return null;
	}

	/// <summary>Whether <paramref name="position"/> is over the rotation handle floating above the selection.</summary>
	public bool HitTestRotateHandle(Point position, Rect imageRect, double scale)
	{
		var element = SelectedElement;
		if (element is null)
			return false;

		var screen = ToScreen(GetRotateHandle(element, scale), imageRect, scale);
		return Math.Abs(position.X - screen.X) <= ThemeDefinition.HandleHitRadius &&
			Math.Abs(position.Y - screen.Y) <= ThemeDefinition.HandleHitRadius;
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

	/// <summary>The document-space position of the rotation handle, floating above the middle of the element's top edge at a constant screen distance.</summary>
	public static SKPoint GetRotateHandle(CanvasElement element, double scale)
	{
		var bounds = element.Bounds;
		var radians = element.Rotation * Math.PI / 180;
		var offset = (float)(ThemeDefinition.RotateHandleOffset / Math.Max(scale, 0.01));
		var outward = new SKPoint(
			(float)(Math.Sin(radians) * offset),
			(float)(-Math.Cos(radians) * offset));
		return element.ToDocument(new SKPoint(bounds.MidX, bounds.Top)) + outward;
	}

	public void Draw(DrawingContext context, Rect imageRect, double scale)
	{
		if (SelectedElement is not { } element)
			return;

		var bounds = element.Bounds;

		//The selection box rotates with the element.
		var box = new StreamGeometry();
		using (var ctx = box.Open())
		{
			ctx.BeginFigure(ToScreen(element.ToDocument(GetCorner(bounds, Corner.TopLeft)), imageRect, scale), true);
			ctx.LineTo(ToScreen(element.ToDocument(GetCorner(bounds, Corner.TopRight)), imageRect, scale));
			ctx.LineTo(ToScreen(element.ToDocument(GetCorner(bounds, Corner.BottomRight)), imageRect, scale));
			ctx.LineTo(ToScreen(element.ToDocument(GetCorner(bounds, Corner.BottomLeft)), imageRect, scale));
			ctx.EndFigure(true);
		}
		context.DrawGeometry(null, ThemeDefinition.SelectionPen, box);

		var half = ThemeDefinition.HandleSize / 2;
		foreach (var corner in AllCorners)
		{
			var screen = ToScreen(element.ToDocument(GetCorner(bounds, corner)), imageRect, scale);
			var handle = new Rect(screen.X - half, screen.Y - half, ThemeDefinition.HandleSize, ThemeDefinition.HandleSize);
			context.FillRectangle(ThemeDefinition.HandleFill, handle);
			context.DrawRectangle(ThemeDefinition.SelectionPen, handle);
		}

		//The rotation handle floats above the middle of the top edge, connected by a short stem.
		var stem = ToScreen(element.ToDocument(new SKPoint(bounds.MidX, bounds.Top)), imageRect, scale);
		var rotate = ToScreen(GetRotateHandle(element, scale), imageRect, scale);
		context.DrawLine(ThemeDefinition.SelectionPen, stem, rotate);

		var ellipse = new Rect(rotate.X - half, rotate.Y - half, ThemeDefinition.HandleSize, ThemeDefinition.HandleSize);
		context.DrawGeometry(ThemeDefinition.HandleFill, ThemeDefinition.SelectionPen, new EllipseGeometry(ellipse));
	}

	private static Point ToScreen(SKPoint point, Rect imageRect, double scale) => new Point(imageRect.X + point.X * scale, imageRect.Y + point.Y * scale);
}
