using WhatSchedule.Elements;

namespace WhatSchedule.Documents
{
	/// <summary>A single reversible change to a document, played back by <see cref="DocumentHistory"/>.</summary>
	internal interface IHistoryAction
	{
		/// <summary>What the change did, shown by the undo and redo commands.</summary>
		string Description { get; }

		void Undo(Document document);

		void Redo(Document document);
	}

	/// <summary>The undo/redo stacks of changes made to a document.</summary>
	public sealed class DocumentHistory
	{
		private readonly Stack<IHistoryAction> undoStack = new();
		private readonly Stack<IHistoryAction> redoStack = new();

		/// <summary>Raised whenever the undo or redo availability changes.</summary>
		public event Action? Changed;

		public bool CanUndo => undoStack.Count > 0;

		public bool CanRedo => redoStack.Count > 0;

		/// <summary>What the next undo will revert, or null when there is nothing to undo.</summary>
		public string? UndoDescription => undoStack.Count > 0 ? undoStack.Peek().Description : null;

		/// <summary>What the next redo will re-apply, or null when there is nothing to redo.</summary>
		public string? RedoDescription => redoStack.Count > 0 ? redoStack.Peek().Description : null;

		/// <summary>Records a new change and discards every change undone before it.</summary>
		internal void Push(IHistoryAction action)
		{
			undoStack.Push(action);
			redoStack.Clear();
			Changed?.Invoke();
		}

		public void Undo(Document document)
		{
			if (undoStack.Count == 0)
				return;

			var action = undoStack.Pop();
			action.Undo(document);
			redoStack.Push(action);
			Changed?.Invoke();
		}

		public void Redo(Document document)
		{
			if (redoStack.Count == 0)
				return;

			var action = redoStack.Pop();
			action.Redo(document);
			undoStack.Push(action);
			Changed?.Invoke();
		}

		public void Clear()
		{
			if (undoStack.Count == 0 && redoStack.Count == 0)
				return;

			undoStack.Clear();
			redoStack.Clear();
			Changed?.Invoke();
		}
	}

	/// <summary>An element entering or leaving a container (the document root or a group).</summary>
	internal sealed class ElementCollectionAction(GroupElement? parent, CanvasElement element, int index, bool added) : IHistoryAction
	{
		public string Description => (added ? "Add " : "Delete ") + element.Kind;

		public void Undo(Document document)
		{
			if (added)
				document.RemoveAt(parent, index);
			else
				document.Insert(parent, element, index);
		}

		public void Redo(Document document)
		{
			if (added)
				document.Insert(parent, element, index);
			else
				document.RemoveAt(parent, index);
		}
	}

	/// <summary>An element swapped for a different one in place, keeping its container and layering slot.</summary>
	internal sealed class ElementReplacementAction(GroupElement? parent, CanvasElement before, CanvasElement after, int index, string description) : IHistoryAction
	{
		public string Description => description;

		public void Undo(Document document)
		{
			document.RemoveAt(parent, index);
			document.Insert(parent, before, index);
		}

		public void Redo(Document document)
		{
			document.RemoveAt(parent, index);
			document.Insert(parent, after, index);
		}
	}

	/// <summary>An element moved within the layering order of its container.</summary>
	internal sealed class ElementReorderAction(CanvasElement element, int before, int after) : IHistoryAction
	{
		public string Description => "Reorder " + element.Kind;

		public void Undo(Document document) => document.MoveTo(element, before);

		public void Redo(Document document) => document.MoveTo(element, after);
	}

	/// <summary>Where an element sat in the hierarchy and the local transform it carried there.</summary>
	internal sealed record ElementParentage(GroupElement? Parent, int Index, SkiaSharp.SKPoint Position, float Rotation);

	/// <summary>
	/// An element moved into or out of a group, with its local transform converted for the new parent and the
	/// chains' scale difference baked into its extent. When it joined a group, the group's content was rebased
	/// around its grown box; the recorded shift moves the children left behind back and forth with the move.
	/// </summary>
	internal sealed class ElementReparentAction(CanvasElement element, ElementParentage before, ElementParentage after, string description, CanvasElement.ElementState stateBefore, CanvasElement.ElementState stateAfter, GroupElement? rebasedGroup = null, SkiaSharp.SKPoint groupPositionBefore = default, SkiaSharp.SKPoint groupPositionAfter = default, SkiaSharp.SKPoint rebaseDelta = default) : IHistoryAction
	{
		public string Description => description;

		public void Undo(Document document)
		{
			document.RestoreParentage(element, before.Parent, before.Index, before.Position, before.Rotation);
			element.RestoreState(stateBefore);
			if (rebasedGroup is { } group && (rebaseDelta.X != 0 || rebaseDelta.Y != 0))
			{
				foreach (var child in group.Children)
					child.Position += rebaseDelta;
				group.Position = groupPositionBefore;
			}
		}

		public void Redo(Document document)
		{
			if (rebasedGroup is { } group && (rebaseDelta.X != 0 || rebaseDelta.Y != 0))
			{
				foreach (var child in group.Children)
					child.Position -= rebaseDelta;
				group.Position = groupPositionAfter;
			}

			document.RestoreParentage(element, after.Parent, after.Index, after.Position, after.Rotation);
			element.RestoreState(stateAfter);
		}
	}

	/// <summary>A group created around two elements; undoing it unpacks the children and removes the group.</summary>
	internal sealed class GroupCreationAction(GroupElement group, CanvasElement first, CanvasElement second, GroupElement? container, int index, ElementParentage firstBefore, ElementParentage secondBefore, ElementParentage firstAfter, ElementParentage secondAfter, string description) : IHistoryAction
	{
		public string Description => description;

		public void Undo(Document document)
		{
			//The group goes first so the children restore into the container exactly as it was before; its
			//index was captured with the children already out of the container. The lower old index is
			//restored first, since the higher slot only lines up once every element below it is back.
			document.RemoveAt(container, index);
			if (firstBefore.Index <= secondBefore.Index)
			{
				document.RestoreParentage(first, firstBefore.Parent, firstBefore.Index, firstBefore.Position, firstBefore.Rotation);
				document.RestoreParentage(second, secondBefore.Parent, secondBefore.Index, secondBefore.Position, secondBefore.Rotation);
			}
			else
			{
				document.RestoreParentage(second, secondBefore.Parent, secondBefore.Index, secondBefore.Position, secondBefore.Rotation);
				document.RestoreParentage(first, firstBefore.Parent, firstBefore.Index, firstBefore.Position, firstBefore.Rotation);
			}
		}

		public void Redo(Document document)
		{
			//The children move back into the group before it is inserted, so its captured index in the
			//container is the one it had with the children already inside it.
			document.RestoreParentage(first, firstAfter.Parent, firstAfter.Index, firstAfter.Position, firstAfter.Rotation);
			document.RestoreParentage(second, secondAfter.Parent, secondAfter.Index, secondAfter.Position, secondAfter.Rotation);
			document.Insert(container, group, index);
		}
	}

	/// <summary>An element renamed in the hierarchy; a null name is the element's kind fallback.</summary>
	internal sealed class ElementRenameAction(CanvasElement element, string? before, string? after) : IHistoryAction
	{
		public string Description => "Rename " + element.Kind;

		public void Undo(Document document)
		{
			element.Name = before;
			document.RaiseChanged();
		}

		public void Redo(Document document)
		{
			element.Name = after;
			document.RaiseChanged();
		}
	}

	/// <summary>Schedule tag assigned to or removed from an element, e.g. a label tagged "Monday: Streaming".</summary>
	internal sealed class ElementTagAction(CanvasElement element, ScheduleTag? before, ScheduleTag? after) : IHistoryAction
	{
		public string Description => "Tag " + element.Kind;

		public void Undo(Document document)
		{
			element.Tag = before;
			document.RaiseChanged();
		}

		public void Redo(Document document)
		{
			element.Tag = after;
			document.RaiseChanged();
		}
	}

	/// <summary>Lock taken from or given to an element, so it refuses to be moved or scaled directly on the canvas.</summary>
	internal sealed class ElementLockAction(CanvasElement element, bool before, bool after) : IHistoryAction
	{
		public string Description => (after ? "Lock " : "Unlock ") + element.Kind;

		public void Undo(Document document)
		{
			element.IsLocked = before;
			document.RaiseChanged();
		}

		public void Redo(Document document)
		{
			element.IsLocked = after;
			document.RaiseChanged();
		}
	}

	/// <summary>Visibility taken from or given to an element, so it is not drawn into the document while the flag lasts.</summary>
	internal sealed class ElementHiddenAction(CanvasElement element, bool before, bool after) : IHistoryAction
	{
		public string Description => (after ? "Hide " : "Show ") + element.Kind;

		public void Undo(Document document)
		{
			element.IsHidden = before;
			document.RaiseChanged();
		}

		public void Redo(Document document)
		{
			element.IsHidden = after;
			document.RaiseChanged();
		}
	}

	/// <summary>An element whose state changed (e.g. by moving or scaling it).</summary>
	internal sealed class ElementStateAction(CanvasElement element, CanvasElement.ElementState before, CanvasElement.ElementState after, string description) : IHistoryAction
	{
		public string Description => description;

		public void Undo(Document document)
		{
			element.RestoreState(before);
			document.RaiseChanged();
		}

		public void Redo(Document document)
		{
			element.RestoreState(after);
			document.RaiseChanged();
		}
	}

	/// <summary>Several changes recorded as one, undone together in reverse order and redone together in their original order.</summary>
	internal sealed class CompositeAction(string description, IReadOnlyList<IHistoryAction> actions) : IHistoryAction
	{
		public string Description => description;

		public void Undo(Document document)
		{
			for (var i = actions.Count - 1; i >= 0; i--)
				actions[i].Undo(document);
		}

		public void Redo(Document document)
		{
			foreach (var action in actions)
				action.Redo(document);
		}
	}
}
