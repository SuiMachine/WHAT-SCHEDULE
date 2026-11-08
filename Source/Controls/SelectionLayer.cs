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

	/// <summary>Whether the crop bounds of the selected image are drawn, i.e. Alt is held or a crop drag is in progress.</summary>
	public bool ShowCropBounds { get; set; }

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
		if (SelectedElement is not null && document is not null && !document.ContainsElement(SelectedElement))
			SelectedElement = null;
	}

	public Corner? HitTestHandle(Point position, Rect imageRect, double scale)
	{
		var element = SelectedElement;
		if (element is null)
			return null;

		var bounds = element.ContentBounds;
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

	/// <summary>The selection box edge <paramref name="position"/> is over, within the handle hit radius; only image elements have a crop to drag.</summary>
	public Edge? HitTestEdge(Point position, Rect imageRect, double scale)
	{
		if (SelectedElement is not ImageElement)
			return null;

		var bounds = SelectedElement.Bounds;
		var topLeft = ToScreen(SelectedElement.ToDocument(GetCorner(bounds, Corner.TopLeft)), imageRect, scale);
		var topRight = ToScreen(SelectedElement.ToDocument(GetCorner(bounds, Corner.TopRight)), imageRect, scale);
		var bottomRight = ToScreen(SelectedElement.ToDocument(GetCorner(bounds, Corner.BottomRight)), imageRect, scale);
		var bottomLeft = ToScreen(SelectedElement.ToDocument(GetCorner(bounds, Corner.BottomLeft)), imageRect, scale);

		var best = (Edge?)null;
		var bestDistance = ThemeDefinition.HandleHitRadius;
		void Check(Edge edge, Point a, Point b)
		{
			var distance = DistanceToSegment(position, a, b);
			if (distance <= bestDistance)
			{
				best = edge;
				bestDistance = distance;
			}
		}

		Check(Edge.Top, topLeft, topRight);
		Check(Edge.Right, topRight, bottomRight);
		Check(Edge.Bottom, bottomRight, bottomLeft);
		Check(Edge.Left, bottomLeft, topLeft);
		return best;
	}

	/// <summary>The shortest distance from <paramref name="point"/> to the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
	private static double DistanceToSegment(Point point, Point a, Point b)
	{
		var dx = b.X - a.X;
		var dy = b.Y - a.Y;
		var lengthSquared = dx * dx + dy * dy;
		var t = lengthSquared > 0 ? Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1) : 0;
		var x = a.X + t * dx;
		var y = a.Y + t * dy;
		return Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
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

	/// <summary>A point on the selection box edge opposite <paramref name="edge"/>, which stays fixed while cropping.</summary>
	public static SKPoint GetCropAnchor(SKRect bounds, Edge edge)
	{
		return edge switch
		{
			Edge.Left => new SKPoint(bounds.Right, bounds.MidY),
			Edge.Top => new SKPoint(bounds.MidX, bounds.Bottom),
			Edge.Right => new SKPoint(bounds.Left, bounds.MidY),
			_ => new SKPoint(bounds.MidX, bounds.Top),
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
		var bounds = element.ContentBounds;
		//The handle floats perpendicular to the edge as it is seen, so the whole ancestor rotation counts, not just the element's own.
		var radians = (element.Rotation + element.ParentRotation) * Math.PI / 180;
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

		var bounds = element.ContentBounds;

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

		//While Alt is held (or a crop drag runs), the dashed box shows the whole bitmap the crop hides.
		if (ShowCropBounds && element is ImageElement image)
		{
			var extent = image.BitmapExtent;
			var cropBounds = new StreamGeometry();
			using (var ctx = cropBounds.Open())
			{
				ctx.BeginFigure(ToScreen(element.ToDocument(GetCorner(extent, Corner.TopLeft)), imageRect, scale), true);
				ctx.LineTo(ToScreen(element.ToDocument(GetCorner(extent, Corner.TopRight)), imageRect, scale));
				ctx.LineTo(ToScreen(element.ToDocument(GetCorner(extent, Corner.BottomRight)), imageRect, scale));
				ctx.LineTo(ToScreen(element.ToDocument(GetCorner(extent, Corner.BottomLeft)), imageRect, scale));
				ctx.EndFigure(true);
			}
			context.DrawGeometry(null, ThemeDefinition.CropBoundsPen, cropBounds);
		}

		//A dashed loop shows where a selected group's vector mask clips its children.
		if (element is GroupElement { Mask: { Count: >= 2 } maskVertices })
		{
			var maskBounds = new StreamGeometry();
			using (var ctx = maskBounds.Open())
			{
				ctx.BeginFigure(ToScreen(element.ToDocument(maskVertices[0]), imageRect, scale), true);
				for (var i = 1; i < maskVertices.Count; i++)
					ctx.LineTo(ToScreen(element.ToDocument(maskVertices[i]), imageRect, scale));
				ctx.EndFigure(true);
			}
			context.DrawGeometry(null, ThemeDefinition.CropBoundsPen, maskBounds);
		}

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
