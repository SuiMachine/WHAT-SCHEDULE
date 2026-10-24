using Avalonia;
using Avalonia.Input;
using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using WhatSchedule.Extensions;

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
	private readonly SKPoint startAnchor;
	private readonly CanvasElement.ElementState startState;

	public ElementMoveDrag(PreviewCanvas canvas, CanvasElement element, Point position)
	{
		this.canvas = canvas;
		this.element = element;
		startPosition = position;
		//The anchor is the element's frame origin in document space; the drag delta is added there and
		//converted into the parent's content space, so elements move correctly inside rotated and scaled groups.
		startAnchor = element.ParentToDocument(element.Position);
		startState = element.CaptureState();
	}

	public Cursor Cursor => CanvasCursors.Move;

	public void Move(Point position, PointerEventArgs e)
	{
		var document = canvas.Document;
		if (document is null)
			return;

		var scale = canvas.Viewport.Scale;
		var delta = new SKPoint(
			(float)((position.X - startPosition.X) / scale),
			(float)((position.Y - startPosition.Y) / scale));
		element.Position = element.DocumentToParent(startAnchor + delta);

		if ((e.KeyModifiers & KeyModifiers.Control) == 0)
			element.Position = element.Position.Snap();

		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		canvas.Document?.RecordStateChange(element, startState, "Move " + element.Kind);
	}
}

internal sealed class ElementScaleDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly Corner corner;
	private readonly SKPoint anchor;
	private readonly SKPoint anchorParent;
	private readonly CanvasElement.ScaleState start;
	private readonly CanvasElement.ElementState startState;

	public ElementScaleDrag(PreviewCanvas canvas, Corner corner)
	{
		this.canvas = canvas;
		this.corner = corner;
		var element = canvas.Selection.SelectedElement!;
		anchor = SelectionLayer.GetAnchor(element.ContentBounds, corner);
		anchorParent = element.ToParentSpace(anchor);
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

		var documentCursor = canvas.Viewport.ScreenToDocument(position, canvas.GetImageRect(document));

		//Scaling happens in the element's unrotated frame: the cursor is rotated into local space,
		//and afterwards the element is shifted so the anchor corner stays pinned at the same point
		//in the parent's space regardless of rotation. Groups take the parent-space cursor instead,
		//because their own scale would feed back into the content-space cursor; they always keep
		//their aspect ratio, so no modifier switches it.
		if (element is GroupElement group)
		{
			var cursorParent = element.DocumentToParent(documentCursor);
			group.ScaleToParent(cursorParent, corner, (GroupElement.GroupScaleState)start);
		}
		else
		{
			element.ScaleTo(element.ToLocal(documentCursor), anchor, start, e);
			var shifted = element.ToParentSpace(anchor);
			if (shifted != anchorParent)
				element.Position += anchorParent - shifted;
		}

		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		if (canvas.Selection.SelectedElement is { } element)
			canvas.Document?.RecordStateChange(element, startState, "Resize " + element.Kind);
	}
}

internal sealed class ElementCropDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly Edge edge;
	private readonly SKPoint anchor;
	private readonly SKPoint anchorParent;
	private readonly ImageElement.ImageCropState start;
	private readonly CanvasElement.ElementState startState;

	public ElementCropDrag(PreviewCanvas canvas, Edge edge)
	{
		this.canvas = canvas;
		this.edge = edge;
		var element = (ImageElement)canvas.Selection.SelectedElement!;
		anchor = SelectionLayer.GetCropAnchor(element.ContentBounds, edge);
		anchorParent = element.ToParentSpace(anchor);
		start = element.BeginCrop();
		startState = element.CaptureState();
	}

	public Cursor Cursor => CanvasCursors.ForEdge(edge);

	public void Move(Point position, PointerEventArgs e)
	{
		var element = canvas.Selection.SelectedElement as ImageElement;
		var document = canvas.Document;
		if (element is null || document is null)
			return;

		//Like scaling, cropping happens in the element's unrotated frame, and the fixed
		//opposite edge is pinned at the same point in the parent's space regardless of rotation.
		var documentCursor = canvas.Viewport.ScreenToDocument(position, canvas.GetImageRect(document));
		element.CropTo(element.ToLocal(documentCursor), edge, start);

		var shifted = element.ToParentSpace(anchor);
		if (shifted != anchorParent)
			element.Position += anchorParent - shifted;

		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		if (canvas.Selection.SelectedElement is { } element)
			canvas.Document?.RecordStateChange(element, startState, "Crop " + element.Kind);
	}
}

internal sealed class ElementRotateDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly CanvasElement element;
	private readonly SKPoint pivot;
	private readonly float startRotation;
	private readonly CanvasElement.ElementState startState;
	private float lastAngle;

	public ElementRotateDrag(PreviewCanvas canvas, Point position)
	{
		this.canvas = canvas;
		element = canvas.Selection.SelectedElement!;
		pivot = element.DocumentPivot;
		startRotation = element.Rotation;
		lastAngle = AngleTo(position);
		startState = element.CaptureState();
	}

	public Cursor Cursor => CanvasCursors.Rotate;

	public void Move(Point position, PointerEventArgs e)
	{
		var document = canvas.Document;
		if (document is null)
			return;

		//The rotation follows the cursor's angle around the center, accumulated per move
		//so drags crossing the -180/180 degree line stay continuous. Shift snaps to steps.
		var angle = AngleTo(position);
		var delta = (angle - lastAngle + 540) % 360 - 180;
		lastAngle = angle;

		var rotation = element.Rotation + delta;
		if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
			rotation = (float)(Math.Round(rotation / ThemeDefinition.RotationSnap) * ThemeDefinition.RotationSnap);

		element.Rotation = rotation;
		document.RaiseChanged();
	}

	public void Release(Point position)
	{
		canvas.Document?.RecordStateChange(element, startState, "Rotate " + element.Kind);
	}

	/// <summary>The angle from the element's rotation pivot to <paramref name="position"/>, in degrees.</summary>
	private float AngleTo(Point position)
	{
		var document = canvas.Document!;
		var point = canvas.Viewport.ScreenToDocument(position, canvas.GetImageRect(document));
		return (float)(Math.Atan2(point.Y - pivot.Y, point.X - pivot.X) * 180 / Math.PI);
	}
}

internal sealed class GuideDrag : IPointerDrag
{
	private readonly PreviewCanvas canvas;
	private readonly Guide guide;

	public GuideDrag(PreviewCanvas canvas, Guide guide)
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
		if (GuideLayer.IsOverRuler(guide, position) && canvas.Document is { } document)
		{
			document.RemoveGuide(guide);
			canvas.InvalidateVisual();
		}
	}
}
