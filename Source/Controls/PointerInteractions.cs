using Avalonia;
using Avalonia.Input;
using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>
/// A single in-progress pointer drag. The canvas picks one from its hit-test
/// chain on pointer press and forwards every move and release to it until the
/// drag ends, so each interaction is self-contained instead of a branch in a
/// shared state machine.
/// </summary>
internal interface IPointerDrag
{
	Cursor Cursor { get; }

	void Move(Point position, PointerEventArgs e);

	void Release(Point position);
}

internal sealed class PanDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly Point startPosition;
	private readonly double startOffsetX;
	private readonly double startOffsetY;

	public PanDrag(PreviewCanvas canvas, Point position)
	{
		this.canvas = canvas;
		startPosition = position;
		startOffsetX = canvas.Viewport.OffsetX;
		startOffsetY = canvas.Viewport.OffsetY;
	}

	public Cursor Cursor => CanvasCursors.Move;

	public void Move(Point position, PointerEventArgs e)
	{
		var document = canvas.Document;
		if (document is null)
			return;

		canvas.Viewport.MoveTo(
			startOffsetX + position.X - startPosition.X,
			startOffsetY + position.Y - startPosition.Y,
			canvas.ContentRect,
			canvas.DocumentSize);
		canvas.InvalidateVisual();
	}

	public void Release(Point position)
	{
	}
}

internal sealed class ElementMoveDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly CanvasElement element;
	private readonly Point startPosition;
	private readonly SKPoint elementStartPosition;
	private readonly CanvasElement.ElementState startState;

	public ElementMoveDrag(PreviewCanvas canvas, CanvasElement element, Point position)
	{
		this.canvas = canvas;
		this.element = element;
		startPosition = position;
		elementStartPosition = element.Position;
		startState = element.CaptureState();
	}

	public Cursor Cursor => CanvasCursors.Move;

	public void Move(Point position, PointerEventArgs e)
	{
		var document = canvas.Document;
		if (document is null)
			return;

		var scale = canvas.Viewport.Scale;
		element.Position = new SKPoint(
			elementStartPosition.X + (float)((position.X - startPosition.X) / scale),
			elementStartPosition.Y + (float)((position.Y - startPosition.Y) / scale));
		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		canvas.Document?.RecordStateChange(element, startState);
	}
}

internal sealed class ElementScaleDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly Corner corner;
	private readonly SKPoint anchor;
	private readonly CanvasElement.ScaleState start;
	private readonly CanvasElement.ElementState startState;

	public ElementScaleDrag(PreviewCanvas canvas, Corner corner)
	{
		this.canvas = canvas;
		this.corner = corner;
		var element = canvas.Selection.SelectedElement!;
		anchor = SelectionLayer.GetAnchor(element.Bounds, corner);
		start = element.BeginScale();
		startState = element.CaptureState();
	}

	public Cursor Cursor => CanvasCursors.ForCorner(corner);

	public void Move(Point position, PointerEventArgs e)
	{
		var element = canvas.Selection.SelectedElement;
		var document = canvas.Document;
		if (element is null || document is null)
			return;

		element.ScaleTo(canvas.Viewport.ScreenToDocument(position, canvas.GetImageRect(document)), anchor, start, e);
		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		if (canvas.Selection.SelectedElement is { } element)
			canvas.Document?.RecordStateChange(element, startState);
	}
}

internal sealed class GuideDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly GuideLine guide;

	public GuideDrag(PreviewCanvas canvas, GuideLine guide)
	{
		this.canvas = canvas;
		this.guide = guide;
	}

	public Cursor Cursor => guide.IsHorizontal ? CanvasCursors.HorizontalGuide : CanvasCursors.VerticalGuide;

	public void Move(Point position, PointerEventArgs e)
	{
		var document = canvas.Document;
		if (document is null)
			return;

		GuideLayer.Move(guide, position, document, canvas.GetImageRect(document), canvas.Viewport.Scale);
		canvas.InvalidateVisual();
	}

	public void Release(Point position)
	{
		//Dropping the guide back onto its ruler removes it.
		if (GuideLayer.IsOverRuler(guide, position))
		{
			canvas.Guides.Remove(guide);
			canvas.InvalidateVisual();
		}
	}
}
