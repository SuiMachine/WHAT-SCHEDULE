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

	public SKBitmap? Background { get; set; }

	public int Width => Background?.Width ?? 0;

	public int Height => Background?.Height ?? 0;

	public IReadOnlyList<CanvasElement> Elements => elements;

	public void Add(CanvasElement element)
	{
		elements.Add(element);
		RaiseChanged();
	}

	public void RaiseChanged() => Changed?.Invoke();
}
