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
	internal SelectionLayer Selection { get; } = new();
	internal CanvasMenus Menus { get; }

	/// <summary>The content area of the control, i.e. everything right of and below the rulers.</summary>
	internal Rect ContentRect => new(ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness,
		Math.Max(0, Bounds.Width - ThemeDefinition.RulerThickness),
		Math.Max(0, Bounds.Height - ThemeDefinition.RulerThickness));

	internal Size DocumentSize => Document is { } document ? new Size(document.Width, document.Height) : default;

	internal Rect GetImageRect(Document document) => Viewport.GetImageRect(ContentRect, new Size(document.Width, document.Height));

	private IPointerDrag? activeDrag;

	/// <summary>The element copied by Ctrl+C or cut, waiting to be pasted.</summary>
	private CanvasElement? clipboardElement;

	/// <summary>How many times the current clipboard content was pasted, so each paste lands one step further from the corner.</summary>
	private int pasteCount;

	/// <summary>The gap between pasted elements, in document pixels.</summary>
	private const float PasteStep = 20f;

	/// <summary>
	/// The group the canvas was drilled into by double-clicking it or selecting its child elsewhere
	/// (e.g. in the hierarchy): single clicks pick elements inside it directly instead of the whole group.
	/// </summary>
	internal GroupElement? DrillGroup { get; set; }

	/// <summary>Raised after the element selected on the canvas changed.</summary>
	internal event Action? SelectionChanged;

	/// <summary>Raised whenever the document content changed, i.e. whenever the canvas re-renders.</summary>
	internal event Action? DocumentChanged;

	public PreviewCanvas()
	{
		Focusable = true;
		Menus = new CanvasMenus(this);

		Selection.SelectionChanged += () =>
		{
			InvalidateVisual();
			SelectionChanged?.Invoke();
		};
		PointerCaptureLost += (_, _) =>
		{
			activeDrag = null;
			Selection.ShowCropBounds = false;
			Cursor = Cursor.Default;
		};
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		var document = Document;
		if (document is null)
			return;

		if (e.Key == Key.Delete)
		{
			if (Selection.SelectedElement is { } element && document.RemoveTracked(element))
				e.Handled = true;
			return;
		}

		//PageUp/PageDown handling for moving selected item up or down in rendering hierarchy
		//The naming is really confusing in Avalonia, but what can you do.
		if (e.Key == Key.Prior || e.Key == Key.Next)
		{
			if (Selection.SelectedElement is { } element && document.ReorderTracked(element, e.Key == Key.Prior ? 1 : -1))
				e.Handled = true;
			return;
		}

		//CTRL+ interactions
		if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			if (e.Key == Key.C)
			{
				CopySelection();
				e.Handled = true;
			}
			else if (e.Key == Key.X)
			{
				CutSelection();
				e.Handled = true;
			}
			else if (e.Key == Key.V)
			{
				PasteClipboard();
				e.Handled = true;
			}
			else if (e.Key == Key.Z)
			{
				//Ctrl+Shift+Z redoes, like Ctrl+Y does.
				if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
					document.History.Redo(document);
				else
					document.History.Undo(document);
				e.Handled = true;
			}
			else if (e.Key == Key.Y)
			{
				document.History.Redo(document);
				e.Handled = true;
			}
		}
	}

	/// <summary>Copies the selected element to the internal clipboard, converting its local transform to the document root; Ctrl+C and Edit &gt; Copy.</summary>
	public bool CopySelection()
	{
		if (Selection.SelectedElement is not { } element)
			return false;

		clipboardElement = CloneAsRoot(element);
		pasteCount = 0;
		return true;
	}

	/// <summary>Copies the selected element to the internal clipboard and removes it from the document; Ctrl+X and Edit &gt; Cut.</summary>
	public bool CutSelection()
	{
		if (Document is not { } document || Selection.SelectedElement is not { } element)
			return false;

		//The clone must be taken while the element still sits in its group, so its local transform can be converted to the root.
		var clone = CloneAsRoot(element);
		if (!document.RemoveTracked(element))
			return false;

		clipboardElement = clone;
		pasteCount = 0;
		return true;
	}

	/// <summary>A copy of <paramref name="element"/> carrying its transform in document space, as it would sit at the root.</summary>
	private static CanvasElement CloneAsRoot(CanvasElement element)
	{
		var clone = element.Clone();
		if (element.Parent is not null)
		{
			//The clone must be taken while the element still sits in its group, so its local transform can be
			//converted to the root. Like Reparent, the pivot carries the transform and the chain's scale is
			//baked into the clone's extent, so the copy keeps both its rendered place and its rendered size.
			var anchor = element.PivotInParent;
			var offset = anchor - element.Position;
			var scale = CanvasElement.AncestorScale(element.Parent);
			clone.BakeScale(scale);
			clone.Rotation = element.Rotation + element.ParentRotation;
			clone.Position = element.ParentToDocument(anchor) - new SKPoint(offset.X * scale.X, offset.Y * scale.Y);
		}

		return clone;
	}

	/// <summary>Pastes the clipboard element in the top left corner, every paste one step further away; Ctrl+V and Edit &gt; Paste.</summary>
	public bool PasteClipboard()
	{
		if (Document is not { } document || clipboardElement is null)
			return false;

		var element = clipboardElement.Clone();
		element.Position = new SKPoint(PasteStep * pasteCount, PasteStep * pasteCount);
		pasteCount++;

		document.AddTracked(element);
		Selection.SelectedElement = element;
		return true;
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
			activeDrag = null;
			InvalidateVisual();
		}
	}

	private void OnDocumentChanged()
	{
		Selection.SyncWithDocument(Document);
		if (DrillGroup is not null && Document is { } document && !document.ContainsElement(DrillGroup))
			DrillGroup = null;
		Surface.MarkDirty();
		InvalidateVisual();
		DocumentChanged?.Invoke();
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

		//The canvas needs keyboard focus for the Delete key to work.
		Focus();

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

		var drag = BeginDrag(point.Position, e.KeyModifiers, e.ClickCount);
		if (drag is null)
			return;

		activeDrag = drag;
		Selection.ShowCropBounds = drag is ElementCropDrag ||
			(e.KeyModifiers.HasFlag(KeyModifiers.Alt) && Selection.SelectedElement is ImageElement);
		e.Pointer.Capture(this);
		Cursor = drag.Cursor;
		InvalidateVisual();
		e.Handled = true;
	}

	/// <summary>Runs the hit-test chain and returns the drag interaction it starts, falling back to panning.</summary>
	private IPointerDrag BeginDrag(Point position, KeyModifiers modifiers, int clickCount)
	{
		var document = Document!;
		var imageRect = GetImageRect(document);

		if (Surface.Bitmap is not null)
		{
			if (Selection.SelectedElement is not null && Selection.HitTestRotateHandle(position, imageRect, Viewport.Scale))
				return new ElementRotateDrag(this, position);

			if (Selection.SelectedElement is not null && Selection.HitTestHandle(position, imageRect, Viewport.Scale) is { } corner)
				return new ElementScaleDrag(this, corner);

			//Alt and an edge of the selection crops the image instead of scaling it.
			if (Selection.SelectedElement is ImageElement && modifiers.HasFlag(KeyModifiers.Alt) &&
				Selection.HitTestEdge(position, imageRect, Viewport.Scale) is { } edge)
				return new ElementCropDrag(this, edge);

			var guide = GuideLayer.CreateFromRuler(position, document, imageRect, Viewport.Scale) ?? GuideLayer.HitTest(position, document, imageRect, Viewport.Scale);
			if (guide is not null)
				return new GuideDrag(this, guide);

			if (HitTestElement(position) is { } hit)
			{
				var target = ResolveSelection(hit, clickCount >= 2);
				Selection.SelectedElement = target;
				return new ElementMoveDrag(this, target, position);
			}
		}

		if (Selection.SelectedElement is not null)
		{
			Selection.SelectedElement = null;
			InvalidateVisual();
		}

		return new PanDrag(this, position);
	}

	/// <summary>
	/// What a canvas click selects: a plain click selects the outermost group around the hit element
	/// (the element itself when ungrouped), a double click drills into the group and selects the hit
	/// element directly, and once drilled in, plain clicks keep picking elements inside that group.
	/// </summary>
	private CanvasElement ResolveSelection(CanvasElement hit, bool drillIn)
	{
		if (drillIn)
		{
			DrillGroup = hit as GroupElement ?? hit.Parent;
			return hit;
		}

		if (DrillGroup is not null && IsWithin(DrillGroup, hit))
			return hit;

		var outer = hit;
		while (outer.Parent is not null)
			outer = outer.Parent;

		DrillGroup = null;
		return outer;
	}

	/// <summary>Whether <paramref name="element"/> is <paramref name="ancestor"/> itself or anywhere inside it.</summary>
	private static bool IsWithin(GroupElement ancestor, CanvasElement element)
	{
		for (var current = element; current is not null; current = current.Parent)
		{
			if (ReferenceEquals(current, ancestor))
				return true;
		}

		return false;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		var position = e.GetPosition(this);
		if (activeDrag is null)
		{
			var showCrop = Selection.ShowCropBounds;
			Selection.ShowCropBounds = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && Selection.SelectedElement is ImageElement;
			if (Selection.ShowCropBounds != showCrop)
				InvalidateVisual();

			UpdateHoverCursor(position, e.KeyModifiers);
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
		EndDrag(e.Pointer, e.KeyModifiers);
		e.Handled = true;
	}

	private void EndDrag(IPointer pointer, KeyModifiers modifiers)
	{
		if (activeDrag is null)
			return;

		activeDrag = null;
		if (pointer.Captured == this)
		{
			pointer.Capture(null);
		}

		var showCrop = modifiers.HasFlag(KeyModifiers.Alt) && Selection.SelectedElement is ImageElement;
		if (Selection.ShowCropBounds != showCrop)
		{
			Selection.ShowCropBounds = showCrop;
			InvalidateVisual();
		}

		Cursor = Cursor.Default;
	}

	private void UpdateHoverCursor(Point position, KeyModifiers modifiers)
	{
		var document = Document;
		if (document is null || Surface.Bitmap is null)
		{
			Cursor = Cursor.Default;
			return;
		}

		var imageRect = GetImageRect(document);
		if (Selection.SelectedElement is not null && Selection.HitTestRotateHandle(position, imageRect, Viewport.Scale))
			Cursor = CanvasCursors.Rotate;
		else if (Selection.SelectedElement is not null && Selection.HitTestHandle(position, imageRect, Viewport.Scale) is { } corner)
			Cursor = CanvasCursors.ForCorner(corner);
		else if (Selection.SelectedElement is ImageElement && modifiers.HasFlag(KeyModifiers.Alt) &&
			Selection.HitTestEdge(position, imageRect, Viewport.Scale) is { } edge)
			Cursor = CanvasCursors.ForEdge(edge);
		else if (GuideLayer.HitTest(position, document, imageRect, Viewport.Scale) is { } guide)
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
		for (var i = document.RootElements.Count - 1; i >= 0; i--)
		{
			if (HitTest(document.RootElements[i], point) is { } element)
				return element;
		}

		return null;
	}

	/// <summary>The topmost element at <paramref name="point"/> (in document pixels): a group's children are hit first, and the empty space inside a group's frame picks the group itself.</summary>
	private static CanvasElement? HitTest(CanvasElement element, SKPoint point)
	{
		if (element is GroupElement group)
		{
			for (var i = group.Children.Count - 1; i >= 0; i--)
			{
				if (HitTest(group.Children[i], point) is { } child)
					return child;
			}

			return group.Contains(point) ? group : null;
		}

		return element.Contains(point) ? element : null;
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

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		if (Surface.EnsureSize(document))
			Viewport.ResetFit();

		Viewport.TryInitialFit(ContentRect, new Size(document.Width, document.Height));
		Surface.Render(document);

		var imageRect = GetImageRect(document);
		context.DrawImage(Surface.Bitmap!, imageRect);
		Selection.Draw(context, imageRect, Viewport.Scale);
		GuideLayer.Draw(context, ContentRect, imageRect, Viewport.Scale, document);
		Rulers.Draw(context, bounds, imageRect, new Size(document.Width, document.Height), Viewport.Scale);
		RulerRenderer.DrawBorders(context, bounds);
	}
}
