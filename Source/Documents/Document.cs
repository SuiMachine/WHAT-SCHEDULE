using SkiaSharp;
using WhatSchedule.Elements;

namespace WhatSchedule.Documents;

/// <summary>
/// The image being designed: a background image plus the elements layered on top of it.
/// Coordinates are document pixels; (0,0) is the top-left corner of the background.
/// The top-level elements form a forest: groups hold their own children and draw them
/// inside their transform.
/// </summary>
public sealed class Document
{
	private readonly List<CanvasElement> rootElements = new();
	private readonly List<Guide> guides = new();

	/// <summary>Raised whenever the document content changes and needs re-rendering.</summary>
	public event Action? Changed;

	/// <summary>Undo/redo history of the changes made to this document.</summary>
	public DocumentHistory History { get; } = new();

	public SKBitmap? Background { get; set; }

	public int Width => Background?.Width ?? 0;

	public int Height => Background?.Height ?? 0;

	/// <summary>The elements at the top level of the hierarchy, in rendering order, bottom first; groups draw their own children.</summary>
	public IReadOnlyList<CanvasElement> RootElements => rootElements;

	/// <summary>User-placed aligning lines in document coordinates; drawn in the editor only.</summary>
	public IReadOnlyList<Guide> Guides => guides;

	/// <summary>The elements that share a parent with <paramref name="element"/>, i.e. its siblings in the hierarchy.</summary>
	public IReadOnlyList<CanvasElement> SiblingsOf(CanvasElement element) => element.Parent?.Children ?? rootElements;

	/// <summary>Whether <paramref name="element"/> is anywhere in the hierarchy, inside any group.</summary>
	public bool ContainsElement(CanvasElement element)
	{
		if (element.Parent is { } parent)
			return parent.IndexOfChild(element) >= 0 && ContainsElement(parent);

		return rootElements.Contains(element);
	}

	/// <summary>Adds an element to the document root without recording it in the undo history.</summary>
	public void Add(CanvasElement element) => Insert(null, element, rootElements.Count);

	/// <summary>Adds an element to the document root and records the addition in the undo history.</summary>
	public void AddTracked(CanvasElement element)
	{
		Insert(null, element, rootElements.Count);
		History.Push(new ElementCollectionAction(null, element, rootElements.Count - 1, added: true));
	}

	/// <summary>Removes an element (with everything inside it) and records the removal in the undo history. Returns false when the element is not in the document.</summary>
	public bool RemoveTracked(CanvasElement element)
	{
		var parent = element.Parent;
		var index = IndexOf(element);
		if (index < 0)
			return false;

		RemoveFrom(element);
		History.Push(new ElementCollectionAction(parent, element, index, added: false));
		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Moves an element in the layering order of its container and records the move in the undo history.
	/// A positive <paramref name="offset"/> moves it towards the front (drawn on top); returns false when it cannot move.
	/// </summary>
	public bool ReorderTracked(CanvasElement element, int offset)
	{
		var index = IndexOf(element);
		var target = index + offset;
		if (index < 0 || target < 0 || target >= SiblingsOf(element).Count)
			return false;

		MoveTo(element, target);
		History.Push(new ElementReorderAction(element, index, target));
		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Moves an element to an absolute position in the layering order of its container and records the move
	/// in the undo history. The <paramref name="index"/> is the element's position among its siblings after
	/// the move; moving an element to its own index changes nothing.
	/// </summary>
	public bool ReorderToTracked(CanvasElement element, int index)
	{
		var current = IndexOf(element);
		if (current < 0)
			return false;

		index = Math.Clamp(index, 0, SiblingsOf(element).Count - 1);
		if (index == current)
			return false;

		MoveTo(element, index);
		History.Push(new ElementReorderAction(element, current, index));
		RaiseChanged();
		return true;
	}

	/// <summary>
	/// Moves <paramref name="element"/> into <paramref name="newParent"/> (or out of every group to the document
	/// root when null) at <paramref name="index"/> among its new siblings, converting its local position and
	/// rotation so it keeps its place on the canvas; records the move in the undo history.
	/// </summary>
	public void ReparentTracked(CanvasElement element, GroupElement? newParent, int index, string description)
	{
		var oldParent = element.Parent;
		var oldIndex = IndexOf(element);
		if (oldIndex < 0 || ReferenceEquals(oldParent, newParent))
			return;

		var oldPosition = element.Position;
		var oldRotation = element.Rotation;
		var stateBefore = element.CaptureState();

		Reparent(element, newParent, index);

		//An element joining a group grows the group's box, so the group's content is rebased to keep
		//the box centered on the content origin, where the group pivots its rotation. The shift is
		//recorded so the move's undo reverts the rebase of the children left behind.
		SKPoint rebaseDelta = SKPoint.Empty;
		SKPoint groupPositionBefore = default;
		SKPoint groupPositionAfter = default;
		if (newParent is { } group)
		{
			groupPositionBefore = group.Position;
			rebaseDelta = group.RebaseToContentBounds();
			groupPositionAfter = group.Position;
		}

		History.Push(new ElementReparentAction(element, new ElementParentage(oldParent, oldIndex, oldPosition, oldRotation), new ElementParentage(newParent, index, element.Position, element.Rotation), description, stateBefore, element.CaptureState(), newParent, groupPositionBefore, groupPositionAfter, rebaseDelta));
		RaiseChanged();
	}

	/// <summary>
	/// Creates a group holding <paramref name="first"/> and <paramref name="second"/> in place of
	/// <paramref name="second"/>'s slot in the hierarchy, keeping both elements visually where they
	/// are, and records the whole creation as one undo step.
	/// </summary>
	public void CreateGroupTracked(CanvasElement first, CanvasElement second)
	{
		var container = second.Parent;
		var firstOld = CaptureParentage(first);
		var secondOld = CaptureParentage(second);

		//The group starts as an identity transform, so the children's new local values are their
		//document-space placement; afterwards the content is rebased so the group's frame starts at its origin.
		var group = new GroupElement();
		Insert(container, group, IndexOf(second));
		Reparent(first, group, 0);
		Reparent(second, group, 1);
		group.RebaseToContentBounds();

		History.Push(new GroupCreationAction(group, first, second, container, IndexOf(group), firstOld, secondOld, CaptureParentage(first), CaptureParentage(second), "Group " + first.Kind + " and " + second.Kind));
		RaiseChanged();
	}

	/// <summary>Renames <paramref name="element"/> and records the rename in the undo history; a null name falls back to the element's kind.</summary>
	public void RenameTracked(CanvasElement element, string? name)
	{
		if (element.Name == name)
			return;

		var old = element.Name;
		element.Name = name;
		History.Push(new ElementRenameAction(element, old, name));
		RaiseChanged();
	}

	/// <summary>
	/// Toggles <paramref name="tag"/> on <paramref name="element"/> and records the change in the undo history:
	/// a tag the element already carries is removed, any other tag replaces the element's single tag.
	/// </summary>
	public void ToggleTagTracked(CanvasElement element, ScheduleTag tag)
	{
		ScheduleTag? after = element.Tag == tag ? null : tag;
		if (element.Tag == after)
			return;

		var before = element.Tag;
		element.Tag = after;
		History.Push(new ElementTagAction(element, before, after));
		RaiseChanged();
	}

	/// <summary>Records an element state change in the undo history, skipping it when the state did not actually change.</summary>
	public void RecordStateChange(CanvasElement element, CanvasElement.ElementState before, string description)
	{
		var after = element.CaptureState();
		if (after.Equals(before))
			return;

		History.Push(new ElementStateAction(element, before, after, description));
	}

	public void RaiseChanged() => Changed?.Invoke();

	/// <summary>Puts <paramref name="element"/> into <paramref name="parent"/> (the root when null) at <paramref name="index"/>, recorded by undo actions.</summary>
	internal void Insert(GroupElement? parent, CanvasElement element, int index)
	{
		if (parent is null)
		{
			element.Parent = null;
			rootElements.Insert(index, element);
		}
		else
		{
			parent.InsertChild(element, index);
		}

		RaiseChanged();
	}

	/// <summary>Removes the element at <paramref name="index"/> from <paramref name="parent"/> (the root when null), recorded by undo actions.</summary>
	internal void RemoveAt(GroupElement? parent, int index)
	{
		if (parent is null)
			rootElements.RemoveAt(index);
		else
			parent.RemoveChild(parent.Children[index]);

		RaiseChanged();
	}

	/// <summary>The index of <paramref name="element"/> among its siblings, or -1 when it is not in its container.</summary>
	internal int IndexOf(CanvasElement element) => element.Parent is { } parent ? parent.IndexOfChild(element) : rootElements.IndexOf(element);

	/// <summary>Restores <paramref name="element"/> into <paramref name="parent"/> at <paramref name="index"/> with the given local transform; the undo/redo of reparenting and grouping.</summary>
	internal void RestoreParentage(CanvasElement element, GroupElement? parent, int index, SKPoint position, float rotation)
	{
		RemoveFrom(element);
		element.Position = position;
		element.Rotation = rotation;
		Insert(parent, element, index);
		RaiseChanged();
	}

	internal void MoveTo(CanvasElement element, int index)
	{
		var parent = element.Parent;
		var current = IndexOf(element);
		if (current < 0 || index < 0 || index >= SiblingsOf(element).Count || index == current)
			return;

		RemoveFrom(element);
		Insert(parent, element, index);
		RaiseChanged();
	}

	/// <summary>Moves <paramref name="element"/> into <paramref name="newParent"/> at <paramref name="index"/>, converting its local transform so it keeps its visual place and size; the pivot, not the position, carries the transform.</summary>
	private void Reparent(CanvasElement element, GroupElement? newParent, int index)
	{
		//A leaf rotates around its box center, so converting the box corner pointwise would shift the
		//element whenever the old or the new parent chain is rotated. The pivot is converted instead,
		//carrying the whole transform with it, and the position is rebuilt from its fixed offset.
		var anchor = element.PivotInParent;
		var offset = anchor - element.Position;
		var documentAnchor = element.ParentToDocument(anchor);
		var documentRotation = element.Rotation + element.ParentRotation;

		//The chains' scale difference is baked into the element's own extent — font and box for labels,
		//frame for images, content scale for groups — so it keeps its rendered size in the new container.
		var oldScale = CanvasElement.AncestorScale(element.Parent);
		var newScale = CanvasElement.AncestorScale(newParent);
		var factor = new SKPoint(oldScale.X / newScale.X, oldScale.Y / newScale.Y);
		element.BakeScale(factor);

		RemoveFrom(element);
		element.Rotation = documentRotation - CanvasElement.AncestorRotation(newParent);
		var anchorNew = CanvasElement.AncestorFromDocument(newParent, documentAnchor);
		element.Position = anchorNew - new SKPoint(offset.X * factor.X, offset.Y * factor.Y);
		Insert(newParent, element, index);
	}

	private void RemoveFrom(CanvasElement element)
	{
		if (element.Parent is { } parent)
			parent.RemoveChild(element);
		else
			rootElements.Remove(element);
	}

	/// <summary>Where <paramref name="element"/> currently sits in the hierarchy and what local transform it carries there.</summary>
	private ElementParentage CaptureParentage(CanvasElement element) => new(element.Parent, IndexOf(element), element.Position, element.Rotation);

	/// <summary>Adds a guide line without recording it in the undo history.</summary>
	public void AddGuide(Guide guide)
	{
		guides.Add(guide);
		RaiseChanged();
	}

	/// <summary>Removes a guide line. Returns false when the guide is not in the document.</summary>
	public bool RemoveGuide(Guide guide)
	{
		if (!guides.Remove(guide))
			return false;

		RaiseChanged();
		return true;
	}

	internal void PrepareNewWithBackground(SKBitmap bitmap)
	{
		this.Background = bitmap;
		rootElements.Clear();
		guides.Clear();
		History.Clear();
		RaiseChanged();
	}
}
