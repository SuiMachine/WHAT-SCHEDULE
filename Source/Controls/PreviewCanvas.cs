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

	private ScheduleActivity? tagFilter;

	/// <summary>
	/// The activity filter of the canvas display: null shows everything, an activity only the elements tagged with it
	/// (and the groups holding one). Display-only: exports render the whole document.
	/// </summary>
	public ScheduleActivity? TagFilter
	{
		get => tagFilter;
		set
		{
			if (tagFilter == value)
				return;

			tagFilter = value;
			if (Selection.SelectedElement is { } selected && !selected.PassesFilter(value))
				Selection.SelectedElement = null;
			Surface.MarkDirty();
			InvalidateVisual();
		}
	}

	private IPointerDrag? activeDrag;

	/// <summary>The element nudged with the arrow keys and its state when the nudging began, so a whole key hold records as one undo entry.</summary>
	private (CanvasElement Element, CanvasElement.ElementState Start)? arrowNudge;

	/// <summary>The arrow keys currently held down; the nudge entry commits when the last one is released.</summary>
	private readonly HashSet<Key> heldArrowKeys = new();

	/// <summary>The element copied by Ctrl+C or cut, waiting to be pasted.</summary>
	private CanvasElement? clipboardElement;

	/// <summary>How many times the current clipboard content was pasted, so each paste lands one step further from the original position.</summary>
	private int pasteCount;

	/// <summary>The gap between pasted elements, in document pixels.</summary>
	private const float PasteStep = 20f;

	/// <summary>
	/// The group the canvas was drilled into by double-clicking it or selecting its child elsewhere
	/// (e.g. in the hierarchy): single clicks pick elements inside it directly instead of the whole group.
	/// </summary>
	internal GroupElement? DrillGroup { get; set; }

	/// <summary>
	/// The group whose vector mask is being edited, or null when no mask is edited: the mask's rectangle and handles
	/// draw instead of the selection, and only the mask responds to the pointer — the elements sit still.
	/// </summary>
	internal GroupElement? MaskEditGroup { get; private set; }

	/// <summary>The group's state when the mask editing began, recorded as the session's single undo entry when the mode is left and the mask changed.</summary>
	private GroupElement.GroupElementState? maskEditStart;

	/// <summary>Whether the editing session created the group's mask: the session's undo entry then says add instead of edit.</summary>
	private bool maskEditCreated;

	/// <summary>
	/// Whether the group changed since the mask editing began: the snapshot's position, rotation, scales and
	/// every vertex compared by value, because record equality would compare the vertex arrays by reference.
	/// </summary>
	private static bool MaskSessionChanged(GroupElement.GroupElementState before, GroupElement.GroupElementState after)
	{
		if (before.Position != after.Position || before.Rotation != after.Rotation ||
			before.ScaleX != after.ScaleX || before.ScaleY != after.ScaleY)
			return true;

		if (before.Mask is null || after.Mask is null)
			return before.Mask is not null || after.Mask is not null;

		if (before.Mask.Length != after.Mask.Length)
			return true;

		for (var i = 0; i < before.Mask.Length; i++)
			if (before.Mask[i] != after.Mask[i])
				return true;

		return false;
	}

	/// <summary>Raised after the mask editing mode started or ended, so the inspector's mask rows can switch their buttons.</summary>
	internal event Action? MaskEditChanged;

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
		//A nudge still open when the selection changes is laid to rest before it could span two elements.
		Selection.SelectionChanged += () => CommitArrowNudge();
		//Selecting something else (or nothing) takes the mask editing mode down with the group.
		Selection.SelectionChanged += () =>
		{
			if (MaskEditGroup is not null && !ReferenceEquals(Selection.SelectedElement, MaskEditGroup))
				EndMaskEdit();
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

		//Escape leaves the mask editing mode, like the inspector's stop button does.
		if (e.Key == Key.Escape && MaskEditGroup is not null)
		{
			EndMaskEdit();
			e.Handled = true;
			return;
		}

		//Arrow keys nudge the selected element by one document pixel per press; holding relies on the keyboard's auto-repeat, which fires this again after its own repeat delay (the "short cooldown").
		if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
		{
			if (Selection.SelectedElement is { } element)
			{
				heldArrowKeys.Add(e.Key);
				arrowNudge ??= (element, element.CaptureState());

				//The move happens in document space, so one arrow pixel is one visual pixel even inside rotated and scaled groups.
				//Unlike dragging there is no snapping: the nudge is exact even from fractional positions.
				var delta = e.Key switch
				{
					Key.Left => new SKPoint(-1f, 0f),
					Key.Right => new SKPoint(1f, 0f),
					Key.Up => new SKPoint(0f, -1f),
					_ => new SKPoint(0f, 1f),
				};
				element.Position = element.DocumentToParent(element.ParentToDocument(element.Position) + delta);
				document.RaiseChanged();
				e.Handled = true;
			}
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
				//Undo or redo would rewrite the edited group mid-session, so the session settles as its own entry first.
				EndMaskEdit();
				//Ctrl+Shift+Z redoes, like Ctrl+Y does.
				if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
					document.History.Redo(document);
				else
					document.History.Undo(document);
				e.Handled = true;
			}
			else if (e.Key == Key.Y)
			{
				EndMaskEdit();
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

	/// <summary>Pastes the clipboard element at its original position, every paste one step further away; Ctrl+V and Edit &gt; Paste.</summary>
	public bool PasteClipboard()
	{
		if (Document is not { } document || clipboardElement is null)
			return false;

		//The first paste lands where the element was copied from; repeats step away so they do not overlap exactly.
		var element = clipboardElement.Clone();
		var step = PasteStep * pasteCount;
		element.Position = new SKPoint(clipboardElement.Position.X + step, clipboardElement.Position.Y + step);
		pasteCount++;

		document.AddTracked(element);
		Selection.SelectedElement = element;
		return true;
	}

	/// <summary>
	/// Enters the group's mask editing mode: a mask covering the group's content is created when it had none,
	/// and the mask's handles replace the selection on the canvas. Leaving the mode records the session as one undo entry.
	/// </summary>
	internal void StartMaskEdit(GroupElement group)
	{
		var document = Document;
		if (document is null || !document.ContainsElement(group) || ReferenceEquals(MaskEditGroup, group))
			return;

		if (MaskEditGroup is not null)
			EndMaskEdit();

		MaskEditGroup = group;
		maskEditStart = group.CaptureState() is GroupElement.GroupElementState start ? start : null;
		maskEditCreated = group.Mask is null;
		if (group.Mask is null)
		{
			//A new mask starts as a rectangle covering everything, so entering the mode alone never hides anything;
			//the corners are its first four vertices, and the edges are then free to grow new ones.
			var content = group.ContentBounds;
			if (content.Width <= 0 || content.Height <= 0)
				content = SKRect.Create(0, 0, 100, 100);
			group.Mask =
			[
				new SKPoint(content.Left, content.Top),
				new SKPoint(content.Right, content.Top),
				new SKPoint(content.Right, content.Bottom),
				new SKPoint(content.Left, content.Bottom),
			];
			document.RaiseChanged();
		}

		MaskEditChanged?.Invoke();
		InvalidateVisual();
	}

	/// <summary>Leaves the mask editing mode, recording the whole session as one undo entry when the group changed during it.</summary>
	internal void EndMaskEdit()
	{
		if (MaskEditGroup is not { } group)
			return;

		MaskEditGroup = null;
		if (Document is { } document && maskEditStart is { } before &&
			group.CaptureState() is GroupElement.GroupElementState after && MaskSessionChanged(before, after))
			document.RecordStateChange(group, before, (maskEditCreated ? "Add " : "Edit ") + group.Kind + " mask");

		MaskEditChanged?.Invoke();
		InvalidateVisual();
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
			MaskEditGroup = null;
			activeDrag = null;
			InvalidateVisual();
		}
	}

	private void OnDocumentChanged()
	{
		Selection.SyncWithDocument(Document);
		if (DrillGroup is not null && Document is { } document && !document.ContainsElement(DrillGroup))
			DrillGroup = null;
		if (MaskEditGroup is not null && Document is { } document2 && !document2.ContainsElement(MaskEditGroup))
			EndMaskEdit();
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

	/// <summary>Runs the hit-test chain and returns the drag interaction it starts, falling back to panning;
	/// null when the press is fully handled already, e.g. a double-click that removes a mask vertex.</summary>
	private IPointerDrag? BeginDrag(Point position, KeyModifiers modifiers, int clickCount)
	{
		var document = Document!;
		var imageRect = GetImageRect(document);

		if (Surface.Bitmap is not null)
		{
			//While a mask is being edited, only the mask responds: vertices drag, a double-click or a Ctrl+click
			//on an edge grows a new vertex there, Ctrl+click on a vertex drops it (keeping at least a triangle,
			//since fewer than three vertices would clip nothing), the interior moves the whole polygon, and
			//everything else pans — the elements underneath sit still.
			if (MaskEditGroup is { } editing)
			{
				if (MaskLayer.HitTestVertex(position, editing, imageRect, Viewport.Scale) is { } vertex)
				{
					if (modifiers.HasFlag(KeyModifiers.Control) && editing.Mask is { Count: > 3 } vertices)
					{
						MaskLayer.RemoveVertex(editing, vertex);
						document.RaiseChanged();
						InvalidateVisual();
						return null;
					}

					return new MaskVertexDrag(this, vertex);
				}

				if (MaskLayer.HitTestEdge(position, editing, imageRect, Viewport.Scale) is { } maskEdge &&
					(clickCount >= 2 || modifiers.HasFlag(KeyModifiers.Control)))
				{
					var cursor = Viewport.ScreenToDocument(position, imageRect);
					return new MaskVertexDrag(this, MaskLayer.InsertVertex(editing, maskEdge, editing.ToLocal(cursor)));
				}

				var maskCursor = Viewport.ScreenToDocument(position, imageRect);
				if (MaskLayer.HitTestInside(maskCursor, editing))
					return new MaskMoveDrag(this, editing, maskCursor);

				return new PanDrag(this, position);
			}

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

	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);

		if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
			return;

		heldArrowKeys.Remove(e.Key);
		if (heldArrowKeys.Count == 0)
			CommitArrowNudge();
	}

	/// <summary>Records the open arrow-key nudge as one move in the undo history and closes it; fire it when the last
	/// arrow is released or when the selection changes mid-nudge, so two elements never share one entry.</summary>
	private void CommitArrowNudge()
	{
		if (arrowNudge is not { } nudge)
			return;

		arrowNudge = null;
		Document?.RecordStateChange(nudge.Element, nudge.Start, "Move " + nudge.Element.Kind);
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

		//While a mask is being edited, the hover cursor follows the mask alone.
		if (MaskEditGroup is { } editing)
		{
			if (MaskLayer.HitTestVertex(position, editing, imageRect, Viewport.Scale) is not null ||
				MaskLayer.HitTestEdge(position, editing, imageRect, Viewport.Scale) is not null ||
				MaskLayer.HitTestInside(Viewport.ScreenToDocument(position, imageRect), editing))
				Cursor = CanvasCursors.Move;
			else
				Cursor = Cursor.Default;

			return;
		}

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
			if (document.RootElements[i].PassesFilter(tagFilter) && HitTest(document.RootElements[i], point, tagFilter) is { } element)
				return element;
		}

		return null;
	}

	/// <summary>The topmost element at <paramref name="point"/> (in document pixels) passing the activity <paramref name="filter"/>: a group's children are hit first, and the empty space inside a group's frame picks the group itself.</summary>
	private static CanvasElement? HitTest(CanvasElement element, SKPoint point, ScheduleActivity? filter)
	{
		if (element is GroupElement group)
		{
			var childFilter = element.ChildFilter(filter);
			for (var i = group.Children.Count - 1; i >= 0; i--)
			{
				if (group.Children[i].PassesFilter(childFilter) && HitTest(group.Children[i], point, childFilter) is { } child)
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
		Surface.Render(document, tagFilter);

		var imageRect = GetImageRect(document);
		context.DrawImage(Surface.Bitmap!, imageRect);
		if (MaskEditGroup is { } editing)
			MaskLayer.Draw(context, editing, imageRect, Viewport.Scale);
		else
			Selection.Draw(context, imageRect, Viewport.Scale);
		GuideLayer.Draw(context, ContentRect, imageRect, Viewport.Scale, document);
		Rulers.Draw(context, bounds, imageRect, new Size(document.Width, document.Height), Viewport.Scale);
		RulerRenderer.DrawBorders(context, bounds);
	}
}
