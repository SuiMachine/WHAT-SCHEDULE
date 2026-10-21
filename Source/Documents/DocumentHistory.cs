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

	/// <summary>An element entering or leaving the document.</summary>
	internal sealed class ElementCollectionAction(CanvasElement element, int index, bool added) : IHistoryAction
	{
		public string Description => (added ? "Add " : "Delete ") + element.Kind;

		public void Undo(Document document)
		{
			if (added)
				document.RemoveAt(index);
			else
				document.Insert(element, index);
		}

		public void Redo(Document document)
		{
			if (added)
				document.Insert(element, index);
			else
				document.RemoveAt(index);
		}
	}

	/// <summary>An element moved within the layering order.</summary>
	internal sealed class ElementReorderAction(CanvasElement element, int before, int after) : IHistoryAction
	{
		public string Description => "Reorder " + element.Kind;

		public void Undo(Document document) => document.MoveTo(element, before);

		public void Redo(Document document) => document.MoveTo(element, after);
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
}
