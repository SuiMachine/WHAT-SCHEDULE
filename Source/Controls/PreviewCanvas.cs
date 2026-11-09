using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System.Linq;
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

	/// <summary>The completion of an ongoing canvas color sampling, awaiting the click that samples or the Escape that cancels it.</summary>
	private TaskCompletionSource<SKColor?>? colorSample;

	/// <summary>The rendered pixels frozen when the color sampling began, in the surface's BGRA-premultiplied layout;
	/// the sampling loupe reads them, since no input but the sampling click can change the document while it runs.</summary>
	private (byte[] Pixels, int Width, int Height)? samplePixels;

	/// <summary>Where the pointer last stood while the color sampling runs, in control pixels; the loupe follows it.</summary>
	private Point? samplePosition;

	/// <summary>The magnification of the sampling loupe's pixel grid.</summary>
	private const int SamplingZoom = 8;

	/// <summary>How many pixels around the cursor the sampling loupe shows, on each side of it.</summary>
	private const int SamplingRadius = 5;

	/// <summary>How far the sampling loupe floats away from the cursor.</summary>
	private const double SamplingLoupeOffset = 16;

	/// <summary>Raised after the mask editing mode started or ended, so the inspector's mask rows can switch their buttons.</summary>
	internal event Action? MaskEditChanged;

	/// <summary>
	/// Waits for the next left click on the canvas and the color it lands on, showing the sampling cursor until
	/// then; null when the click samples nothing (no document) or Escape cancels the sampling.
	/// </summary>
	internal Task<SKColor?> SampleCanvasColor()
	{
		if (colorSample is { } pending)
			return pending.Task;

		if (Document is not { } document || document.Width <= 0 || document.Height <= 0)
			return Task.FromResult<SKColor?>(null);

		//The sampling owns the pointer alone: a mask edit session still open settles as its own undo entry first.
		EndMaskEdit();
		//The loupe shows the pixels the click will sample, so they are rendered up to date and then frozen.
		Surface.Render(document, tagFilter);
		samplePixels = Surface.SnapshotPixels();
		samplePosition = null;
		colorSample = new TaskCompletionSource<SKColor?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Cursor = CanvasCursors.Sample;
		InvalidateVisual();
		return colorSample.Task;
	}

	/// <summary>Ends the color sampling, handing <paramref name="sampled"/> to the awaiting picker; null cancels it.</summary>
	private void FinishColorSample(SKColor? sampled)
	{
		if (colorSample is not { } pending)
			return;

		colorSample = null;
		samplePixels = null;
		samplePosition = null;
		Cursor = Cursor.Default;
		pending.TrySetResult(sampled);
		InvalidateVisual();
	}

	/// <summary>The sampling loupe: the frozen pixels around the cursor magnified beside it, the center cell
	/// boxing the pixel the click will sample.</summary>
	private void DrawSamplingLoupe(DrawingContext context, Rect imageRect)
	{
		if (samplePixels is not { } pixels || samplePosition is not { } position)
			return;

		//The loupe centers on the pixel the click samples, the one under the cursor.
		var documentPoint = Viewport.ScreenToDocument(position, imageRect);
		var centerX = Math.Clamp((int)Math.Round(documentPoint.X), 0, pixels.Width - 1);
		var centerY = Math.Clamp((int)Math.Round(documentPoint.Y), 0, pixels.Height - 1);

		var size = (SamplingRadius * 2 + 1) * SamplingZoom;
		var left = position.X + SamplingLoupeOffset;
		var top = position.Y + SamplingLoupeOffset;
		//The loupe flips to the other side of the cursor when it runs out of room, so it never falls off the canvas.
		if (left + size > Bounds.Width - 2)
			left = position.X - SamplingLoupeOffset - size;
		if (top + size > Bounds.Height - 2)
			top = position.Y - SamplingLoupeOffset - size;
		left = Math.Max(2, left);
		top = Math.Max(2, top);

		var brushes = new Dictionary<Color, IBrush>();
		for (var dy = -SamplingRadius; dy <= SamplingRadius; dy++)
		{
			for (var dx = -SamplingRadius; dx <= SamplingRadius; dx++)
			{
				var color = SampleCell(pixels, centerX + dx, centerY + dy);
				if (!brushes.TryGetValue(color, out var brush))
					brushes[color] = brush = new ImmutableSolidColorBrush(color);
				context.FillRectangle(brush, new Rect(
					left + (dx + SamplingRadius) * SamplingZoom,
					top + (dy + SamplingRadius) * SamplingZoom,
					SamplingZoom, SamplingZoom));
			}
		}

		context.DrawRectangle(ThemeDefinition.SelectionPen, new Rect(left, top, size, size));
		context.DrawRectangle(ThemeDefinition.SelectionPen, new Rect(
			left + SamplingRadius * SamplingZoom,
			top + SamplingRadius * SamplingZoom,
			SamplingZoom, SamplingZoom));
	}

	/// <summary>The frozen snapshot's pixel at (<paramref name="x"/>, <paramref name="y"/>) as a straight-alpha
	/// color to brush the loupe's cell with, with coordinates clamped into the snapshot.</summary>
	private static Color SampleCell((byte[] Pixels, int Width, int Height) pixels, int x, int y)
	{
		var px = Math.Clamp(x, 0, pixels.Width - 1);
		var py = Math.Clamp(y, 0, pixels.Height - 1);
		var offset = (py * pixels.Width + px) * 4;
		var alpha = pixels.Pixels[offset + 3];
		if (alpha == 0)
			return Color.FromArgb(0, 0, 0, 0);

		return Color.FromArgb(alpha,
			(byte)Math.Min(byte.MaxValue, pixels.Pixels[offset + 2] * 255 / alpha),
			(byte)Math.Min(byte.MaxValue, pixels.Pixels[offset + 1] * 255 / alpha),
			(byte)Math.Min(byte.MaxValue, pixels.Pixels[offset] * 255 / alpha));
	}

	/// <summary>Raised after the element selected on the canvas changed.</summary>
	internal event Action? SelectionChanged;

	/// <summary>Raised whenever the document content changed, i.e. whenever the canvas re-renders.</summary>
	internal event Action? DocumentChanged;

	public PreviewCanvas()
	{
		Focusable = true;
		Menus = new CanvasMenus(this);

		//Image files dragged from elsewhere land as image elements at the pixel they are dropped on.
		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
		AddHandler(DragDrop.DropEvent, OnFileDrop, RoutingStrategies.Bubble);

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

		//Escape cancels a pending color sample, like the mask editing mode below, which it also ends.
		if (e.Key == Key.Escape && colorSample is not null)
		{
			FinishColorSample(null);
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
			//A locked element stays where it is; the lock protects it even from the keyboard.
			if (Selection.SelectedElement is { } element && !element.IsLocked)
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
			//A pending color sample cannot outlive the document it was to sample from.
			FinishColorSample(null);
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

	/// <summary>Accepts a drag hovering over the canvas while it drags files; the copy effect tells the source the drop will use them.</summary>
	private void OnDragOver(object? sender, DragEventArgs e)
	{
		e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	/// <summary>
	/// Drops the image files dragged onto the canvas: each becomes an image element at the document pixel it landed
	/// on, every next file one step away so a batch does not stack into one invisible pile; the last one selects.
	/// A file that is no image or cannot be read is skipped, so one bad file does not stop the rest.
	/// </summary>
	private async void OnFileDrop(object? sender, DragEventArgs e)
	{
		e.Handled = true;
		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		//The dragged items are read before the first await, so the drag's payload cannot be disposed mid-loop.
		var files = new List<IStorageFile>();
		foreach (var item in e.DataTransfer.Items)
		{
			if (item.TryGetFile() is IStorageFile file)
				files.Add(file);
		}

		var position = Viewport.ScreenToDocument(e.GetPosition(this), GetImageRect(document));
		ImageElement? last = null;
		var step = 0;
		foreach (var file in files)
		{
			SKBitmap? bitmap;
			try
			{
				await using var stream = await file.OpenReadAsync();
				bitmap = SKBitmap.Decode(stream);
			}
			catch
			{
				continue;
			}

			if (bitmap is null)
				continue;

			//The file's own name names the element, so a dropped batch does not show as a wall of "Image".
			var image = new ImageElement(bitmap, position + new SKPoint(PasteStep * step, PasteStep * step)) { Name = file.Name };
			document.AddTracked(image);
			last = image;
			step++;
		}

		if (last is not null)
		{
			Selection.SelectedElement = last;
			Focus();
			InvalidateVisual();
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		//The canvas needs keyboard focus for the Delete key to work.
		Focus();

		var point = e.GetCurrentPoint(this);

		//A pending color sample owns the pointer: the next left click samples the pixel under it, and any other click does nothing.
		if (colorSample is not null)
		{
			if (point.Properties.IsLeftButtonPressed && Document is { } samplingDocument)
				FinishColorSample(Surface.SamplePixel(Viewport.ScreenToDocument(point.Position, GetImageRect(samplingDocument))));

			e.Handled = true;
			return;
		}

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
						//The inspector's point rows follow the vertex count, so they rebuild with one vertex less.
						MaskEditChanged?.Invoke();
						InvalidateVisual();
						return null;
					}

					return new MaskVertexDrag(this, vertex);
				}

				if (MaskLayer.HitTestEdge(position, editing, imageRect, Viewport.Scale) is { } maskEdge &&
					(clickCount >= 2 || modifiers.HasFlag(KeyModifiers.Control)))
				{
					var cursor = Viewport.ScreenToDocument(position, imageRect);
					var inserted = MaskLayer.InsertVertex(editing, maskEdge, editing.ToLocal(cursor));

					//The new vertex grows the point rows the same moment it becomes draggable.
					MaskEditChanged?.Invoke();
					return new MaskVertexDrag(this, inserted);
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

				//A locked element selects — its inspector can still be edited — but it does not follow
				//the pointer: pressing it pans the view instead. Unlock it in the hierarchy's row menu.
				return target.IsLocked ? new PanDrag(this, position) : new ElementMoveDrag(this, target, position);
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

		//While a color sampling waits, the pointer sweep only carries the loupe across the canvas.
		if (colorSample is not null)
		{
			samplePosition = position;
			InvalidateVisual();
			return;
		}

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
		//A pending color sample keeps its cursor everywhere it sweeps.
		if (colorSample is not null)
		{
			Cursor = CanvasCursors.Sample;
			return;
		}

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
		else if (HitTestElement(position) is { } hit && !hit.IsLocked)
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
				if (group.Children[i].IsDisplayed(childFilter) && HitTest(group.Children[i], point, childFilter) is { } child)
					return child;
			}

			return group.Contains(point) ? group : null;
		}

		return element.Contains(point) ? element : null;
	}

	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);

		//The loupe leaves with the pointer, until it comes back to finish the sampling.
		if (colorSample is not null && samplePosition is not null)
		{
			samplePosition = null;
			InvalidateVisual();
		}
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

		//The loupe rides above everything, next to the cursor it follows.
		if (colorSample is not null)
			DrawSamplingLoupe(context, imageRect);
	}
}
