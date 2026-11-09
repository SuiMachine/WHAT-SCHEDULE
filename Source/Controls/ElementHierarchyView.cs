using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;
using WhatSchedule.Controls.Dialogs;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using static WhatSchedule.ThemeDefinition.HierarchyView;

namespace WhatSchedule.Controls;

/// <summary>
/// The document's element tree, docked under the inspector: the topmost element of each container is
/// listed first with a group's children indented below it, and a group's chevron hides or shows them,
/// since group contents are often many. The top edge resizes the view, clicking a row selects
/// the element on the canvas, and dragging a row either drops it on the insertion line between rows — landing
/// at that exact spot, which reorders it among its own level, moves it into the group whose elements surround
/// the spot, or leaves its group for the root level — or onto a row: anywhere on a group's row it joins
/// that group at the top, onto a plain row's mid band the two are wrapped in a new group. Right-clicking
/// a row opens a menu that tags, locks, renames or deletes its element; a locked element cannot be moved
/// or scaled directly on the canvas, only carried by its group. Every change is recorded in the document's undo history.
/// </summary>
public sealed class ElementHierarchyView : Control
{
	/// <summary>One display row of the tree: the element, its nesting depth, its container and its position in the container's rendering order.</summary>
	private sealed record HierarchyRow(CanvasElement Element, int Depth, GroupElement? Container, int DrawIndex);

	private PreviewCanvas? canvas;
	private readonly List<HierarchyRow> rows = new();
	private readonly List<FormattedText> labels = new();

	/// <summary>The tag, rename and delete menu opened by right-clicking a row; it acts on the element it was opened for.</summary>
	private readonly ContextMenu rowMenu;

	/// <summary>The tag check items of the row menu, kept so the menu can be re-checked for the element it is opened on.</summary>
	private readonly List<KeyValuePair<MenuItem, ScheduleTag>> tagItems = new();

	/// <summary>The day items of the tag menu, kept so their headers can announce the element's tag.</summary>
	private readonly List<KeyValuePair<MenuItem, ScheduleDay>> dayItems = new();

	/// <summary>The element the row menu was opened for, kept because the menu outlives the rows it was built from.</summary>
	private CanvasElement? menuElement;

	/// <summary>The lock item of the row menu, kept so the menu can be re-checked for the element it is opened on.</summary>
	private readonly MenuItem lockItem;

	/// <summary>The vertical scroll offset of the list, in pixels, when it outgrows the view.</summary>
	private double scrollOffset;

	/// <summary>The row under the pointer, in display order, or -1 when it is outside the list.</summary>
	private int hoveredRow = -1;

	/// <summary>The row being dragged, in display order, or -1 when no drag is running.</summary>
	private int dragRow = -1;

	/// <summary>The element of the row being dragged, kept because <see cref="dragRow"/> goes away with the drag.</summary>
	private CanvasElement? dragElement;

	/// <summary>The groups whose rows the list hides until their chevron is clicked again; the choice survives
	/// refreshes and selection changes, but not a document load. Empty groups never hide anything and are never in it.</summary>
	private readonly HashSet<GroupElement> collapsedGroups = new();

	/// <summary>The row the dragged element would be dropped onto, or null while it is not over a row's drop band.</summary>
	private HierarchyRow? dropRow;

	/// <summary>The display boundary the dragged row would land on when released, or -1 before the drag threshold is crossed or while over a drop band.</summary>
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

	/// <summary>Whether the pointer currently hovers the new group button in the header band.</summary>
	private bool buttonHover;

	public ElementHierarchyView()
	{
		//The tag menu nests the fourteen tags under their days, so picking a tag reads as "on this day, this happens".
		var tagsItem = new MenuItem { Header = "Tags" };
		foreach (var day in Enum.GetValues<ScheduleDay>())
		{
			var dayItem = new MenuItem { Header = day.ToString() };
			dayItems.Add(new(dayItem, day));
			foreach (var activity in Enum.GetValues<ScheduleActivity>())
			{
				var tag = new ScheduleTag(day, activity);
				var activityItem = new MenuItem { Header = ScheduleTag.ActivityName(activity), ToggleType = MenuItemToggleType.CheckBox };
				activityItem.Click += (_, _) => ToggleTag(tag);
				tagItems.Add(new(activityItem, tag));
				dayItem.Items.Add(activityItem);
			}

			tagsItem.Items.Add(dayItem);
		}

		var lockItem = new MenuItem { Header = "Lock", ToggleType = MenuItemToggleType.CheckBox };
		lockItem.Click += (_, _) => ToggleLock();
		this.lockItem = lockItem;

		var renameItem = new MenuItem { Header = "Rename..." };
		renameItem.Click += async (_, _) => await RenameElementAsync();

		var deleteItem = new MenuItem { Header = "Delete" };
		deleteItem.Click += (_, _) => DeleteElement();

		rowMenu = new ContextMenu();
		rowMenu.Items.Add(tagsItem);
		rowMenu.Items.Add(new Separator());
		rowMenu.Items.Add(lockItem);
		rowMenu.Items.Add(renameItem);
		rowMenu.Items.Add(deleteItem);

		//A lost capture (a dialog opening, the window losing focus) must not leave a drag or resize half-finished.
		PointerCaptureLost += (_, _) =>
		{
			dragRow = -1;
			dragElement = null;
			dropRow = null;
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
		//A selection made on the canvas lands in the list: the groups above the selected element open and its row scrolls into view.
		canvas.SelectionChanged += RefreshRevealingSelection;
		canvas.DocumentChanged += Refresh;
		canvas.PropertyChanged += OnCanvasPropertyChanged;
		Refresh();
	}

	/// <summary>Rebuilds the list when another document was loaded into the bound canvas.</summary>
	private void OnCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
	{
		if (change.Property == PreviewCanvas.DocumentProperty)
		{
			//The groups of one document say nothing about the groups of another.
			collapsedGroups.Clear();
			Refresh();
		}
	}

	/// <summary>Rebuilds the list after the canvas selection changed, showing the selected element's row from
	/// wherever it hides: the groups collapsed above the selection open, and a row outside the viewport scrolls in.</summary>
	private void RefreshRevealingSelection()
	{
		var selected = canvas?.Selection.SelectedElement;
		for (var ancestor = selected?.Parent; ancestor is not null; ancestor = ancestor.Parent)
			collapsedGroups.Remove(ancestor);

		Refresh();
		ScrollSelectedIntoView(selected);
	}

	/// <summary>Scrolls <paramref name="selected"/>'s row into view when it lies outside, so the eye lands on the
	/// row canvas clicks select; a row that already shows leaves the scroll where the user put it.</summary>
	private void ScrollSelectedIntoView(CanvasElement? selected)
	{
		if (selected is null)
			return;

		var row = -1;
		for (var i = 0; i < rows.Count; i++)
		{
			if (ReferenceEquals(rows[i].Element, selected))
			{
				row = i;
				break;
			}
		}

		if (row < 0)
			return;

		var viewport = Math.Max(0, Bounds.Height - ListTop);
		var top = row * RowHeight - scrollOffset;
		if (top >= 0 && top + RowHeight <= viewport)
			return;

		//The row was above the viewport, so it scrolls to the viewport's top; below, to the viewport's bottom.
		scrollOffset = top < 0 ? row * RowHeight : (row + 1) * RowHeight - viewport;
		scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(0, rows.Count * RowHeight - viewport));
	}

	/// <summary>Rebuilds the element rows from the current document; the selected row is matched live while rendering.</summary>
	private void Refresh()
	{
		rows.Clear();
		labels.Clear();
		hoveredRow = -1;
		dragRow = -1;
		dragElement = null;
		dropRow = null;
		dragTarget = -1;

		var roots = canvas?.Document?.RootElements;
		if (roots is not null)
			Build(roots, null, 0);

		//Groups that left the document (deleted or replaced) take their choice with them.
		collapsedGroups.IntersectWith(rows.Select(row => row.Element).OfType<GroupElement>());

		InvalidateVisual();
	}

	/// <summary>Adds the container's elements topmost first, a group's children indented below its own row.</summary>
	private void Build(IReadOnlyList<CanvasElement> container, GroupElement? parent, int depth)
	{
		for (var i = container.Count - 1; i >= 0; i--)
		{
			var element = container[i];
			rows.Add(new HierarchyRow(element, depth, parent, i));
			labels.Add(CreateLabel(element));

			//A collapsed group lists nothing: its rows hide until its chevron is clicked again.
			if (element is GroupElement group && !collapsedGroups.Contains(group))
				Build(group.Children, group, depth + 1);
		}
	}

	/// <summary>The row's display name; group rows are bold so containers stand out from their contents, and locked rows are dimmed, since they sit still.</summary>
	private static FormattedText CreateLabel(CanvasElement element)
	{
		var typeface = element is GroupElement ? GroupTypeface : ThemeDefinition.LabelTypeface;
		return new FormattedText(element.DisplayName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, element.IsLocked ? ThemeDefinition.RulerTick : ThemeDefinition.RulerLabel);
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
		DrawNewGroupButton(context, bounds.Size);
		context.DrawLine(SeparatorPen, new Point(0, ListTop - 0.5), new Point(bounds.Width, ListTop - 0.5));

		if (rows.Count == 0)
		{
			context.DrawText(EmptyHint, new Point(TextPadding, ListTop + 4));
			return;
		}

		CanvasElement? selected = canvas?.Selection.SelectedElement;
		double viewportHeight = Math.Max(0, bounds.Height - ListTop);
		scrollOffset = Math.Clamp(scrollOffset, 0, Math.Max(0, rows.Count * RowHeight - viewportHeight));

		using (context.PushClip(new Rect(0, ListTop, bounds.Width, viewportHeight)))
		{
			var firstRow = (int)Math.Floor(scrollOffset / RowHeight);
			var lastRow = Math.Min(rows.Count - 1, (int)Math.Floor((scrollOffset + viewportHeight) / RowHeight));
			for (var row = firstRow; row <= lastRow; row++)
			{
				var top = ListTop + row * RowHeight - scrollOffset;
				var rowBounds = new Rect(0, top, bounds.Width, RowHeight);

				if (ReferenceEquals(rows[row].Element, selected))
					context.FillRectangle(SelectedRowFill, rowBounds);
				else if (dropRow is { } target && ReferenceEquals(rows[row], target))
					context.FillRectangle(DropRowFill, rowBounds);
				else if (row == hoveredRow && dragRow < 0)
					context.FillRectangle(HoverRowFill, rowBounds);

				if (row < rows.Count - 1)
					context.DrawLine(SeparatorPen, new Point(0, top + RowHeight - 0.5), new Point(bounds.Width, top + RowHeight - 0.5));

				//Every row's name begins one indent in, reserving the level's slot before it for a group's chevron.
				var gauge = TextPadding + rows[row].Depth * IndentWidth;
				var indent = gauge + IndentWidth;
				if (rows[row].Element is GroupElement { Children.Count: > 0 } groupRow)
				{
					using (context.PushTransform(Matrix.CreateTranslation(gauge + (IndentWidth - ChevronSize) / 2, top + (RowHeight - ChevronSize) / 2)))
						context.DrawGeometry(null, ChevronPen, collapsedGroups.Contains(groupRow) ? CollapsedChevron : ExpandedChevron);
				}

				DrawName(context, labels[row], new Rect(indent, top, Math.Max(0, bounds.Width - indent - TextPadding), RowHeight));
			}

			if (dragRow >= 0 && dragTarget >= 0)
			{
				//The line sits on the display boundary the dragged row would land on when released.
				var y = ListTop + Math.Min(dragTarget, rows.Count) * RowHeight - scrollOffset;
				y = Math.Clamp(y, ListTop, ListTop + Math.Min(rows.Count * RowHeight, viewportHeight));
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

	/// <summary>Draws the button at the right end of the header band, which adds a new empty group to the document.</summary>
	private void DrawNewGroupButton(DrawingContext context, Size view)
	{
		var button = NewGroupButtonBounds(view);
		context.FillRectangle(buttonHover ? SelectedRowFill : ThemeDefinition.Border, button);

		//The plus sign marks the button as an "add" action.
		var center = button.Center;
		context.DrawLine(PlusPen, new Point(center.X - NewGroupPlusArm, center.Y), new Point(center.X + NewGroupPlusArm, center.Y));
		context.DrawLine(PlusPen, new Point(center.X, center.Y - NewGroupPlusArm), new Point(center.X, center.Y + NewGroupPlusArm));
	}

	/// <summary>The bounds of the new group button in the header band, in view coordinates.</summary>
	private static Rect NewGroupButtonBounds(Size view) =>
		new(view.Width - NewGroupButtonWidth - TextPadding, GripHeight + (HeaderHeight - NewGroupButtonHeight) / 2, NewGroupButtonWidth, NewGroupButtonHeight);

	/// <summary>Adds a new empty group on top of the hierarchy and selects it; elements can then be dragged onto its row to fill it.</summary>
	private void CreateGroup()
	{
		if (canvas?.Document is not { } document)
			return;

		var group = new GroupElement();
		document.AddTracked(group);
		canvas.Selection.SelectedElement = group;
		canvas.DrillGroup = null;
		canvas.Focus();
		canvas.InvalidateVisual();
	}

	/// <summary>Selects the pressed row like a left press would and opens its rename/delete menu at the pointer; returns false when the press hit no row.</summary>
	private bool ShowRowMenu(Point position)
	{
		if (RowAt(position) is not { } row || canvas is not { } owner)
			return false;

		//The row is selected like a left press would, so the menu and the canvas agree on the element.
		var element = rows[row].Element;
		owner.Selection.SelectedElement = element;
		owner.DrillGroup = element.Parent;
		menuElement = element;

		//The tag items are re-checked every time, because the menu outlives the elements it was opened for.
		foreach (var pair in tagItems)
			pair.Key.IsChecked = element.Tag == pair.Value;

		//The lock item is re-checked every time, for the same reason the tag items are.
		lockItem.IsChecked = element.IsLocked;

		//A day the element is tagged with shows the full tag in its header, so the choice is visible without opening the day.
		foreach (var pair in dayItems)
			pair.Key.Header = element.Tag is { } tag && tag.Day == pair.Value ? tag.DisplayName : pair.Value.ToString();

		//Reopening a menu that is already open must close it first, or the new position is ignored; same workaround as the canvas menus.
		if (rowMenu.IsOpen)
		{
			rowMenu.Close();
			Dispatcher.UIThread.Post(() => rowMenu.Open(this));
		}
		else
			rowMenu.Open(this);

		return true;
	}

	/// <summary>Toggles <paramref name="tag"/> on the menu's element, recorded in the undo history; the element carries at most one tag, so any other replaces it.</summary>
	private void ToggleTag(ScheduleTag tag)
	{
		if (menuElement is { } element && canvas?.Document is { } document)
			document.ToggleTagTracked(element, tag);
	}

	/// <summary>Toggles the menu's element's lock, recorded in the undo history; a locked element refuses to move,
	/// scale or rotate directly on the canvas but still rides along with its group's own transform.</summary>
	private void ToggleLock()
	{
		if (menuElement is { } element && canvas?.Document is { } document)
			document.SetLockTracked(element, !element.IsLocked);
	}

	/// <summary>Asks for a new name for the menu's element and records the rename in the undo history; an empty answer falls back to the element's kind.</summary>
	private async Task RenameElementAsync()
	{
		if (menuElement is not { } element || canvas?.Document is not { } document)
			return;

		if (TopLevel.GetTopLevel(this) is not Window window)
			return;

		var name = await TextPromptDialog.ShowDialog(window, "Rename", "Name", element.DisplayName);
		if (name is null)
			return;

		document.RenameTracked(element, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
	}

	/// <summary>Removes the menu's element, with everything inside it, from the document; recorded in the undo history.</summary>
	private void DeleteElement()
	{
		if (menuElement is not { } element || canvas?.Document is not { } document)
			return;

		document.RemoveTracked(element);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);

		if (point.Properties.IsRightButtonPressed)
		{
			if (ShowRowMenu(point.Position))
				e.Handled = true;
			return;
		}

		if (!point.Properties.IsLeftButtonPressed)
			return;

		if (NewGroupButtonBounds(Bounds.Size).Contains(point.Position))
		{
			CreateGroup();
			e.Handled = true;
			InvalidateVisual();
			return;
		}

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

		if (RowAt(point.Position) is not { } row || canvas is not { } owner)
			return;

		//A click on a group's chevron hides or shows its rows instead of selecting or dragging it.
		if (rows[row].Element is GroupElement { Children.Count: > 0 } toggleGroup && IsOverChevron(point.Position, row))
		{
			ToggleCollapse(toggleGroup);
			e.Handled = true;
			return;
		}

		//Pressing a row selects it right away, like pressing the element on the canvas does; the
		//canvas also takes focus, so the delete and layering shortcuts keep working. Selecting a
		//group's member drills the canvas into that group, so a click there picks it directly.
		//The element is read before the selection assignment, which refreshes this list and could shift the row index.
		var element = rows[row].Element;
		owner.Selection.SelectedElement = element;
		owner.DrillGroup = element.Parent;
		owner.Focus();
		dragRow = row;
		dragElement = element;
		dropRow = null;
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
			if (dropRow is null && dragTarget < 0 && Math.Abs(position.Y - dragStart.Y) < DragThreshold)
				return;

			UpdateDrop(position);
			e.Handled = true;
			InvalidateVisual();
			return;
		}

		UpdateHover(position);
	}

	/// <summary>Decides what the dragged element would land on at <paramref name="position"/>: a group's
	/// whole row takes it into the group, a plain row's drop band wraps it with that row's element in a new
	/// group, and anywhere else it lands on the boundary between rows.</summary>
	private void UpdateDrop(Point position)
	{
		dropRow = null;
		dragTarget = -1;

		if (RowAt(position) is { } row)
		{
			//A group's whole row takes the dragged element in, so aiming at the group from anywhere on it joins.
			if (rows[row].Element is GroupElement group && CanDropOnGroup(group))
			{
				dropRow = rows[row];
				return;
			}

			if (IsValidDropTarget(rows[row].Element))
			{
				//Only the band around a row's vertical center drops onto the row; the edges drop between rows instead.
				var offsetInRow = position.Y - (ListTop + row * RowHeight - scrollOffset);
				if (offsetInRow >= RowHeight * (1 - DropBand) / 2 && offsetInRow <= RowHeight * (1 + DropBand) / 2)
				{
					dropRow = rows[row];
					return;
				}
			}
		}

		dragTarget = (int)Math.Clamp(Math.Round((position.Y - ListTop + scrollOffset) / RowHeight), 0, rows.Count);
	}

	/// <summary>Whether the dragged element may land on <paramref name="group"/>'s row: not itself and not one of
	/// the groups nested inside it; a member of the group lands back in it, at its top.</summary>
	private bool CanDropOnGroup(GroupElement group) =>
		dragElement is not null && !ReferenceEquals(dragElement, group) && !IsDescendantOf(group, dragElement);

	/// <summary>Whether the dragged element may land on <paramref name="element"/>: not itself and not one of its own contents.</summary>
	private bool IsValidDropTarget(CanvasElement element) =>
		dragElement is not null && !ReferenceEquals(dragElement, element) && !IsDescendantOf(dragElement, element);

	/// <summary>Whether <paramref name="element"/> lies inside <paramref name="ancestor"/> or any of its groups.</summary>
	private static bool IsDescendantOf(CanvasElement element, CanvasElement ancestor)
	{
		for (var current = element; current is not null; current = current.Parent)
		{
			if (ReferenceEquals(current, ancestor))
				return true;
		}

		return false;
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

		var element = dragElement;
		var target = dropRow;
		var boundary = dragTarget;
		dragRow = -1;
		dragElement = null;
		dropRow = null;
		dragTarget = -1;
		if (e.Pointer.Captured == this)
			e.Pointer.Capture(null);

		if (element is not null && target is { } row)
			DropOnRow(element, row.Element);
		else if (element is not null && boundary >= 0)
			DropAtBoundary(element, boundary);

		e.Handled = true;
		InvalidateVisual();
	}

	/// <summary>Drops <paramref name="element"/> onto <paramref name="target"/>'s row: a group takes it into
	/// the group's top — a member already in it just moves up to that top — and a plain element is wrapped in a new group with it.</summary>
	private void DropOnRow(CanvasElement element, CanvasElement target)
	{
		if (canvas?.Document is not { } document)
			return;

		if (target is GroupElement group)
		{
			//Dropping onto a collapsed group shows its contents again, so the newcomer is where the eye lands.
			collapsedGroups.Remove(group);

			//A member of the group is already in it; landing on the group's head moves it to the group's top.
			if (element.Parent == group)
			{
				document.ReorderToTracked(element, group.Children.Count - 1);
				return;
			}

			document.ReparentTracked(element, group, group.Children.Count, "Group " + element.Kind);
			return;
		}

		//Dropping onto a plain element wraps the two in a new group, which becomes the selection.
		document.CreateGroupTracked(element, target);
		canvas.Selection.SelectedElement = target.Parent;
		canvas.DrillGroup = null;
	}

	/// <summary>
	/// Drops <paramref name="element"/> on the display <paramref name="boundary"/>: it lands exactly on
	/// the insertion line, above the row below it — at its own level that reorders, at another level it moves
	/// into the group there between its elements, and at the root level it leaves its group, keeping its place on the canvas.
	/// </summary>
	private void DropAtBoundary(CanvasElement element, int boundary)
	{
		if (canvas?.Document is not { } document)
			return;

		//The container and index the boundary points at: the row below the boundary decides where in
		//its container the element lands, and below the whole list is the bottom of the root level.
		GroupElement? container;
		int index;
		if (boundary >= rows.Count)
		{
			container = null;
			index = 0;
		}
		else
		{
			container = rows[boundary].Container;
			//The list shows a container's children topmost first, the reverse of their index order, so
			//landing on the line means one index after the row below it: directly above it in the list.
			index = rows[boundary].DrawIndex + 1;
		}

		if (element.Parent == container)
		{
			//The boundary directly above the dragged element's own row drops it back where it was.
			if (boundary < rows.Count && ReferenceEquals(rows[boundary].Element, element))
				return;

			var current = document.IndexOf(element);
			//The target index counts the element still sitting in the container; moving up means one slot less.
			if (current >= 0 && current < index)
				index--;

			document.ReorderToTracked(element, index);
			return;
		}

		//A reparent must not drop an element into itself or into a group nested inside its own contents;
		//deeper levels are fine to leave, so a member of a nested group can rise into the surrounding group.
		if (container is not null && (ReferenceEquals(element, container) || IsDescendantOf(container, element)))
			return;

		var description = container is null ? "Ungroup " + element.Kind : "Group " + element.Kind;
		document.ReparentTracked(element, container, index, description);
	}

	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);

		if (dragRow >= 0 || resizing)
			return;

		if (hoveredRow != -1 || gripHover || buttonHover)
		{
			hoveredRow = -1;
			gripHover = false;
			buttonHover = false;
			Cursor = Cursor.Default;
			InvalidateVisual();
		}
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		e.Handled = true;

		var max = Math.Max(0, rows.Count * RowHeight - Math.Max(0, Bounds.Height - ListTop));
		if (max <= 0)
			return;

		scrollOffset = Math.Clamp(scrollOffset - e.Delta.Y * RowHeight * 2, 0, max);
		InvalidateVisual();
	}

	/// <summary>Updates the hovered row, the new group button, the resize band hover and the cursor from the pointer position.</summary>
	private void UpdateHover(Point position)
	{
		//The button sits in the header band, above the list, so it outranks both the rows and the resize band.
		var overButton = NewGroupButtonBounds(Bounds.Size).Contains(position);
		var row = overButton ? null : RowAt(position);
		var overGrip = !overButton && position.Y <= GripHeight;
		if (row == hoveredRow && overGrip == gripHover && overButton == buttonHover)
			return;

		hoveredRow = row ?? -1;
		gripHover = overGrip;
		buttonHover = overButton;
		Cursor = overGrip ? ResizeCursor : row is not null || overButton ? HandCursor : Cursor.Default;
		InvalidateVisual();
	}

	/// <summary>The display row at the position, or null when it is outside the list.</summary>
	private int? RowAt(Point position)
	{
		if (rows.Count == 0 || position.X < 0 || position.X > Bounds.Width)
			return null;

		var row = (int)Math.Floor((position.Y - ListTop + scrollOffset) / RowHeight);
		return row >= 0 && row < rows.Count ? row : null;
	}

	/// <summary>Whether <paramref name="position"/> lies on the row's chevron slot, the indent the row's name follows after.</summary>
	private bool IsOverChevron(Point position, int row)
	{
		var gauge = TextPadding + rows[row].Depth * IndentWidth;
		return position.X >= gauge && position.X < gauge + IndentWidth;
	}

	/// <summary>Hides or shows the group's rows, keeping the choice across refreshes until the document changes.</summary>
	private void ToggleCollapse(GroupElement group)
	{
		if (!collapsedGroups.Remove(group))
			collapsedGroups.Add(group);
		Refresh();
	}
}
