using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using WhatSchedule.Controls.ContextMenus;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>
/// The interactive schedule preview. Composes the viewport, preview surface,
/// rulers, guides, selection and menus, and routes pointer input into
/// <see cref="IPointerDrag"/> interactions picked by the hit-test chain.
/// </summary>
public class PreviewCanvas : Control, IDisposable
{
	public static readonly StyledProperty<Document?> DocumentProperty = AvaloniaProperty.Register<PreviewCanvas, Document?>(nameof(Document));

	public Document? Document
	{
		get => GetValue(DocumentProperty);
		set => SetValue(DocumentProperty, value);
	}

	internal CanvasViewport Viewport { get; } = new();
	internal PreviewSurface Surface { get; } = new();
	internal RulerRenderer Rulers { get; } = new();
	internal GuideLayer Guides { get; } = new();
	internal SelectionLayer Selection { get; } = new();
	internal CanvasMenus Menus { get; }

	/// <summary>The content area of the control, i.e. everything right of and below the rulers.</summary>
	internal Rect ContentRect => new(ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness,
		Math.Max(0, Bounds.Width - ThemeDefinition.RulerThickness),
		Math.Max(0, Bounds.Height - ThemeDefinition.RulerThickness));

	internal Size DocumentSize => Document is { } document ? new Size(document.Width, document.Height) : default;

	internal Rect GetImageRect(Document document) => Viewport.GetImageRect(ContentRect, new Size(document.Width, document.Height));

	private IPointerDrag? activeDrag;

	public PreviewCanvas()
	{
		Menus = new CanvasMenus(this);
		PointerCaptureLost += (_, _) =>
		{
			activeDrag = null;
			Cursor = Cursor.Default;
		};
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == DocumentProperty)
		{
			if (change.OldValue is Document oldDocument)
				oldDocument.Changed -= OnDocumentChanged;
			if (change.NewValue is Document newDocument)
				newDocument.Changed += OnDocumentChanged;

			Surface.Dispose();
			Viewport.ResetFit();
			Selection.SelectedElement = null;
			Guides.Clear();
			activeDrag = null;
			InvalidateVisual();
		}
	}

	private void OnDocumentChanged()
	{
		Selection.SyncWithDocument(Document);
		Surface.MarkDirty();
		InvalidateVisual();
	}

	public void Dispose()
	{
		if (Document is { } document)
			document.Changed -= OnDocumentChanged;

		Surface.Dispose();
		GC.SuppressFinalize(this);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);
		if (point.Properties.IsRightButtonPressed)
		{
			if (Menus.ShowRulerMenu(point.Position) || Menus.ShowCanvasMenu(point.Position))
			{
				e.Handled = true;
			}

			return;
		}

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0 || !point.Properties.IsLeftButtonPressed)
			return;

		var drag = BeginDrag(point.Position);
		if (drag is null)
			return;

		activeDrag = drag;
		e.Pointer.Capture(this);
		Cursor = drag.Cursor;
		InvalidateVisual();
		e.Handled = true;
	}

	/// <summary>Runs the hit-test chain and returns the drag interaction it starts, falling back to panning.</summary>
	private IPointerDrag BeginDrag(Point position)
	{
		var document = Document!;
		var imageRect = GetImageRect(document);

		if (Surface.Bitmap is not null)
		{
			if (Selection.SelectedElement is not null && Selection.HitTestHandle(position, imageRect, Viewport.Scale) is { } corner)
				return new ElementScaleDrag(this, corner);

			var guide = Guides.CreateFromRuler(position, document, imageRect, Viewport.Scale) ?? Guides.HitTest(position, imageRect, Viewport.Scale);
			if (guide is not null)
				return new GuideDrag(this, guide);

			if (HitTestElement(position) is { } element)
			{
				Selection.SelectedElement = element;
				return new ElementMoveDrag(this, element, position);
			}
		}

		if (Selection.SelectedElement is not null)
		{
			Selection.SelectedElement = null;
			InvalidateVisual();
		}

		return new PanDrag(this, position);
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		var position = e.GetPosition(this);
		if (activeDrag is null)
		{
			UpdateHoverCursor(position);
			return;
		}

		activeDrag.Move(position, e);
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		if (e.Properties.IsRightButtonPressed)
			return;
		base.OnPointerReleased(e);
		activeDrag?.Release(e.GetPosition(this));
		EndDrag(e.Pointer);
		e.Handled = true;
	}

	private void EndDrag(IPointer pointer)
	{
		if (activeDrag is null)
			return;

		activeDrag = null;
		if (pointer.Captured == this)
		{
			pointer.Capture(null);
		}

		Cursor = Cursor.Default;
	}

	private void UpdateHoverCursor(Point position)
	{
		var document = Document;
		if (document is null || Surface.Bitmap is null)
		{
			Cursor = Cursor.Default;
			return;
		}

		var imageRect = GetImageRect(document);
		if (Selection.SelectedElement is not null && Selection.HitTestHandle(position, imageRect, Viewport.Scale) is { } corner)
			Cursor = CanvasCursors.ForCorner(corner);
		else if (Guides.HitTest(position, imageRect, Viewport.Scale) is { } guide)
			Cursor = guide.IsHorizontal ? CanvasCursors.HorizontalGuide : CanvasCursors.VerticalGuide;
		else if (HitTestElement(position) is not null)
			Cursor = CanvasCursors.Move;
		else
			Cursor = Cursor.Default;
	}

	private CanvasElement? HitTestElement(Point position)
	{
		var document = Document;
		if (document is null || Surface.Bitmap is null)
			return null;

		var point = Viewport.ScreenToDocument(position, GetImageRect(document));
		for (var i = document.Elements.Count - 1; i >= 0; i--)
		{
			if (document.Elements[i].Contains(point))
				return document.Elements[i];
		}

		return null;
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		e.Handled = true;

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		var factor = Math.Clamp(Math.Pow(ThemeDefinition.WheelFactor, e.Delta.Y), 1 / 1.5, 1.5);
		if (Viewport.Zoom(factor, e.GetPosition(this), ContentRect, new Size(document.Width, document.Height)))
			InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		var bounds = new Rect(Bounds.Size);
		context.FillRectangle(ThemeDefinition.Background, bounds);
		RulerRenderer.DrawBorders(context, bounds);

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		if (Surface.EnsureSize(document))
		{
			Viewport.ResetFit();
			Guides.Clear();
		}

		Viewport.TryInitialFit(ContentRect, new Size(document.Width, document.Height));
		Surface.Render(document);

		var imageRect = GetImageRect(document);
		context.DrawImage(Surface.Bitmap!, imageRect);
		Selection.Draw(context, imageRect, Viewport.Scale);
		Guides.Draw(context, ContentRect, imageRect, Viewport.Scale);
		Rulers.Draw(context, bounds, imageRect, new Size(document.Width, document.Height), Viewport.Scale);
	}
}
