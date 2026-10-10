using SkiaSharp;
using WhatSchedule.Elements;

namespace WhatSchedule.Documents;

/// <summary>
/// The image being designed: a background image plus the elements layered on top of it.
/// Coordinates are document pixels; (0,0) is the top-left corner of the background.
/// </summary>
public sealed class Document
{
	private readonly List<CanvasElement> elements = new();

	/// <summary>Raised whenever the document content changes and needs re-rendering.</summary>
	public event Action? Changed;

	/// <summary>Undo/redo history of the changes made to this document.</summary>
	public DocumentHistory History { get; } = new();

	public SKBitmap? Background { get; set; }

	public int Width => Background?.Width ?? 0;

	public int Height => Background?.Height ?? 0;

	public IReadOnlyList<CanvasElement> Elements => elements;

	/// <summary>Adds an element without recording it in the undo history.</summary>
	public void Add(CanvasElement element)
	{
		elements.Add(element);
		RaiseChanged();
	}

	/// <summary>Adds an element and records the addition in the undo history.</summary>
	public void AddTracked(CanvasElement element)
	{
		elements.Add(element);
		History.Push(new ElementCollectionAction(element, elements.Count - 1, added: true));
		RaiseChanged();
	}

	/// <summary>Removes an element and records the removal in the undo history. Returns false when the element is not in the document.</summary>
	public bool RemoveTracked(CanvasElement element)
	{
		var index = elements.IndexOf(element);
		if (index < 0)
			return false;

		elements.RemoveAt(index);
		History.Push(new ElementCollectionAction(element, index, added: false));
		RaiseChanged();
		return true;
	}

	/// <summary>Records an element state change in the undo history, skipping it when the state did not actually change.</summary>
	public void RecordStateChange(CanvasElement element, CanvasElement.ElementState before)
	{
		var after = element.CaptureState();
		if (after.Equals(before))
			return;

		History.Push(new ElementStateAction(element, before, after));
	}

	public void RaiseChanged() => Changed?.Invoke();

	internal void Insert(CanvasElement element, int index)
	{
		elements.Insert(index, element);
		RaiseChanged();
	}

	internal void RemoveAt(int index)
	{
		elements.RemoveAt(index);
		RaiseChanged();
	}

	internal void PrepareNewWithBackground(SKBitmap bitmap)
	{
		this.Background = bitmap;
		elements.Clear();
		History.Clear();
		RaiseChanged();
	}
}
