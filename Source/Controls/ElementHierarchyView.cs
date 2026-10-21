using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System.Globalization;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>
/// The list of the document's elements in the order they are rendered, topmost first, docked under
/// the inspector. The top edge resizes the view, clicking a row selects the element on the canvas,
/// and dragging a row to another position changes the rendering order, recorded in the document's
/// undo history.
/// </summary>
public sealed class ElementHierarchyView : Control
{
	/// <summary>The height of the resize band at the top edge, in pixels.</summary>
	private const double GripHeight = 6;

	/// <summary>The height of the header band between the resize band and the list, in pixels.</summary>
	private const double HeaderHeight = 20;

	/// <summary>The distance of the first row from the top edge, in pixels.</summary>
	private const double ListTop = GripHeight + HeaderHeight;

	/// <summary>The height of one element row, in pixels.</summary>
	private const double RowHeight = 20;

	/// <summary>The font size of the header and the element names.</summary>
	private const double FontSize = 12;

	/// <summary>The horizontal padding of the drawn texts, in pixels.</summary>
	private const double TextPadding = 8;

	/// <summary>How far the pointer must move before a pressed row turns into a drag, in pixels.</summary>
	private const double DragThreshold = 3;

	/// <summary>The smallest height the top edge can shrink the view to, in pixels.</summary>
	private const double MinViewHeight = 80;

	/// <summary>The height left to the inspector above when the view is grown the largest, in pixels.</summary>
	private const double MinSiblingHeight = 120;

	/// <summary>The highlight of the row of the element selected on the canvas.</summary>
	private static readonly ISolidColorBrush SelectedRowFill = new ImmutableSolidColorBrush(ThemeDefinition.Selection.Color, 0.2);

	/// <summary>The highlight of the row under the pointer.</summary>
	private static readonly ISolidColorBrush HoverRowFill = new ImmutableSolidColorBrush(Colors.White, 0.13);

	/// <summary>The pen of the lines separating the bands and the rows.</summary>
	private static readonly IPen SeparatorPen = new Pen(ThemeDefinition.Border, 1);

	/// <summary>The pen of the insertion line shown while a row is dragged.</summary>
	private static readonly IPen InsertionPen = new Pen(ThemeDefinition.Selection, 1);

	private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
	private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeNorthSouth);

	private static readonly FormattedText HeaderLabel = new("Elements", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, FontSize, ThemeDefinition.RulerLabel);

	/// <summary>The hint shown while the document has no elements, e.g. before a schedule is loaded.</summary>
	private static readonly FormattedText EmptyHint = new("No elements.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, FontSize, ThemeDefinition.RulerTick);

	private static readonly FormattedText Ellipsis = new("…", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, FontSize, ThemeDefinition.RulerLabel);

	private PreviewCanvas? canvas;
	private readonly List<FormattedText> labels = new();

	/// <summary>The vertical scroll offset of the list, in pixels, when it outgrows the view.</summary>
	private double scrollOffset;

	/// <summary>The row under the pointer, in display order, or -1 when it is outside the list.</summary>
	private int hoveredRow = -1;

	/// <summary>The row being dragged, in display order, or -1 when no drag is running.</summary>
	private int dragRow = -1;

	/// <summary>The display boundary the dragged row would land on when released, or -1 before the drag threshold is crossed.</summary>
	private int dragTarget = -1;

	/// <summary>The pointer position where the pressed row started, to tell a click from a drag.</summary>
	private Point dragStart;

	/// <summary>Whether the pointer is currently dragging the top edge to resize the view.</summary>
	private bool resizing;

	/// <summary>The view's height when the resize started, in pixels.</summary>
	private double resizeStartHeight;

	/// <summary>The pointer position where the resize started.</summary>
	private Point resizeStart;

	/// <summary>Whether the pointer currently hovers the resize band.</summary>
	private bool gripHover;

	public ElementHierarchyView()
	{
		//A lost capture (a dialog opening, the window losing focus) must not leave a drag or resize half-finished.
		PointerCaptureLost += (_, _) =>
		{
			dragRow = -1;
			dragTarget = -1;
			resizing = false;
			InvalidateVisual();
		};
	}

	/// <summary>Connects the list to a canvas and its selection and document changes.</summary>
	public void Bind(PreviewCanvas owner)
	{
		if (canvas is not null)
			throw new InvalidOperationException("The hierarchy view is already bound to a canvas.");

		canvas = owner;
		canvas.SelectionChanged += Refresh;
		canvas.DocumentChanged += Refresh;
		canvas.PropertyChanged += OnCanvasPropertyChanged;
		Refresh();
	}

	/// <summary>Rebuilds the list when another document was loaded into the bound canvas.</summary>
	private void OnCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
	{
		if (change.Property == PreviewCanvas.DocumentProperty)
			Refresh();
	}

	/// <summary>Rebuilds the element names from the current document; the selected row is matched live while rendering.</summary>
	private void Refresh()
	{
		labels.Clear();
		hoveredRow = -1;
		dragRow = -1;
		dragTarget = -1;

		var elements = canvas?.Document?.Elements;
		if (elements is not null)
		{
			//The list shows the topmost element first, i.e. the reverse of the rendering order.
			for (var i = elements.Count - 1; i >= 0; i--)
				labels.Add(new FormattedText(elements[i].DisplayName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, FontSize, ThemeDefinition.RulerLabel));
		}

		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		var bounds = new Rect(Bounds.Size);
		context.FillRectangle(ThemeDefinition.RulerBackground, bounds);

		//The resize band reads as the view's top edge: a hairline that lights up on hover and while resizing.
		context.DrawLine(SeparatorPen, new Point(0, GripHeight - 0.5), new Point(bounds.Width, GripHeight - 0.5));
		if (gripHover || resizing)
			context.DrawLine(InsertionPen, new Point(0, GripHeight - 0.5), new Point(bounds.Width, GripHeight - 0.5));

		context.DrawText(HeaderLabel, new Point(TextPadding, GripHeight + (HeaderHeight - HeaderLabel.Height) / 2));
		context.DrawLine(SeparatorPen, new Point(0, ListTop - 0.5), new Point(bounds.Width, ListTop - 0.5));

		if (labels.Count == 0)
		{
			context.DrawText(EmptyHint, new Point(TextPadding, ListTop + 4));
			return;
		}

		var selected = canvas?.Selection.SelectedElement;
		var elements = canvas?.Document?.Elements;
		var viewportHeight = Math.Max(0, bounds.Height - ListTop);
		scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(0, labels.Count * RowHeight - viewportHeight));

		using (context.PushClip(new Rect(0, ListTop, bounds.Width, viewportHeight)))
		{
			var firstRow = (int)Math.Floor(scrollOffset / RowHeight);
			var lastRow = Math.Min(labels.Count - 1, (int)Math.Floor((scrollOffset + viewportHeight) / RowHeight));
			for (var row = firstRow; row <= lastRow; row++)
			{
				var top = ListTop + row * RowHeight - scrollOffset;
				var rowBounds = new Rect(0, top, bounds.Width, RowHeight);

				if (elements is not null && ReferenceEquals(elements[elements.Count - 1 - row], selected))
					context.FillRectangle(SelectedRowFill, rowBounds);
				else if (row == hoveredRow && dragRow < 0)
					context.FillRectangle(HoverRowFill, rowBounds);

				if (row < labels.Count - 1)
					context.DrawLine(SeparatorPen, new Point(0, top + RowHeight - 0.5), new Point(bounds.Width, top + RowHeight - 0.5));

				DrawName(context, labels[row], new Rect(TextPadding, top, Math.Max(0, bounds.Width - TextPadding * 2), RowHeight));
			}

			if (dragRow >= 0 && dragTarget >= 0)
			{
				//The line sits on the display boundary the dragged row would land on when released.
				var y = ListTop + Math.Min(dragTarget, labels.Count) * RowHeight - scrollOffset;
				y = Math.Clamp(y, ListTop, ListTop + Math.Min(labels.Count * RowHeight, viewportHeight));
				context.DrawLine(InsertionPen, new Point(0, y), new Point(bounds.Width, y));
				context.FillRectangle(ThemeDefinition.Selection, new Rect(0, y - 1.5, 3, 3));
				context.FillRectangle(ThemeDefinition.Selection, new Rect(bounds.Width - 3, y - 1.5, 3, 3));
			}
		}
	}

	/// <summary>Draws the element name vertically centered in its row, with an ellipsis when it does not fit.</summary>
	private static void DrawName(DrawingContext context, FormattedText name, Rect row)
	{
		if (name.WidthIncludingTrailingWhitespace <= row.Width)
		{
			context.DrawText(name, new Point(row.X, row.Y + (row.Height - name.Height) / 2));
			return;
		}

		//The name is clipped one ellipsis short of the row and the ellipsis drawn into the freed space.
		using (context.PushClip(new Rect(row.X, row.Y, Math.Max(0, row.Width - Ellipsis.Width), row.Height)))
			context.DrawText(name, new Point(row.X, row.Y + (row.Height - name.Height) / 2));
		context.DrawText(Ellipsis, new Point(row.X + row.Width - Ellipsis.Width, row.Y + (row.Height - Ellipsis.Height) / 2));
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);
		if (!point.Properties.IsLeftButtonPressed)
			return;

		if (point.Position.Y <= GripHeight)
		{
			resizing = true;
			resizeStartHeight = Height;
			resizeStart = point.Position;
			Cursor = ResizeCursor;
			e.Pointer.Capture(this);
			e.Handled = true;
			InvalidateVisual();
			return;
		}

		if (RowAt(point.Position) is not { } row || ElementAt(row) is not { } element || canvas is not { } owner)
			return;

		//Pressing a row selects it right away, like pressing the element on the canvas does; the
		//canvas also takes focus, so the delete and layering shortcuts keep working.
		owner.Selection.SelectedElement = element;
		owner.Focus();
		dragRow = row;
		dragTarget = -1;
		dragStart = point.Position;
		e.Pointer.Capture(this);
		e.Handled = true;
		InvalidateVisual();
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		var position = e.GetPosition(this);
		if (resizing)
		{
			//The view grows by the distance the top edge was pulled down from, leaving the inspector room above.
			var maxHeight = Math.Max(MinViewHeight, (Parent as Visual)?.Bounds.Height ?? 0) - MinSiblingHeight;
			Height = Math.Clamp(resizeStartHeight + resizeStart.Y - position.Y, MinViewHeight, Math.Max(MinViewHeight, maxHeight));
			e.Handled = true;
			return;
		}

		if (dragRow >= 0)
		{
			//The drag starts only once the pressed row moved; until then the press stays a click.
			if (dragTarget < 0 && Math.Abs(position.Y - dragStart.Y) < DragThreshold)
				return;

			dragTarget = (int)Math.Clamp(Math.Round((position.Y - ListTop + scrollOffset) / RowHeight), 0, labels.Count);
			e.Handled = true;
			InvalidateVisual();
			return;
		}

		UpdateHover(position);
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);

		if (resizing)
		{
			resizing = false;
			if (e.Pointer.Captured == this)
				e.Pointer.Capture(null);

			e.Handled = true;
			InvalidateVisual();
			return;
		}

		if (dragRow < 0)
			return;

		var element = ElementAt(dragRow);
		var boundary = dragTarget;
		dragRow = -1;
		dragTarget = -1;
		if (e.Pointer.Captured == this)
			e.Pointer.Capture(null);

		//The display boundary the row was dropped on becomes the element's new position in the rendering order.
		if (element is not null && boundary >= 0 && canvas?.Document is { } document)
			document.ReorderToTracked(element, labels.Count - 1 - Math.Min(boundary, labels.Count - 1));

		e.Handled = true;
		InvalidateVisual();
	}

	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);

		if (dragRow >= 0 || resizing)
			return;

		if (hoveredRow != -1 || gripHover)
		{
			hoveredRow = -1;
			gripHover = false;
			Cursor = Cursor.Default;
			InvalidateVisual();
		}
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		e.Handled = true;

		var max = Math.Max(0, labels.Count * RowHeight - Math.Max(0, Bounds.Height - ListTop));
		if (max <= 0)
			return;

		scrollOffset = Math.Clamp(scrollOffset - e.Delta.Y * RowHeight * 2, 0, max);
		InvalidateVisual();
	}

	/// <summary>Updates the hovered row, the resize band hover and the cursor from the pointer position.</summary>
	private void UpdateHover(Point position)
	{
		var row = RowAt(position);
		var overGrip = position.Y <= GripHeight;
		if (row == hoveredRow && overGrip == gripHover)
			return;

		hoveredRow = row ?? -1;
		gripHover = overGrip;
		Cursor = overGrip ? ResizeCursor : row is not null ? HandCursor : Cursor.Default;
		InvalidateVisual();
	}

	/// <summary>The display row at the position, or null when it is outside the list.</summary>
	private int? RowAt(Point position)
	{
		if (labels.Count == 0 || position.X < 0 || position.X > Bounds.Width)
			return null;

		var row = (int)Math.Floor((position.Y - ListTop + scrollOffset) / RowHeight);
		return row >= 0 && row < labels.Count ? row : null;
	}

	/// <summary>The element shown in the display row, or null when the row is outside the list.</summary>
	private CanvasElement? ElementAt(int row)
	{
		var elements = canvas?.Document?.Elements;
		if (elements is null || (uint)row >= (uint)elements.Count)
			return null;

		return elements[elements.Count - 1 - row];
	}
}
