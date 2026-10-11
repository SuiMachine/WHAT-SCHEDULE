// If there is a class that has too many summeries - it's this one
// But it is an abstract base class, so I am willing to accept it - Sui
using Avalonia.Input;
using SkiaSharp;

namespace WhatSchedule.Elements;

/// <summary>
/// Base class for everything rendered in the canvas
/// </summary>
public abstract class CanvasElement
{
	/// <summary>The position of the element's top-left corner in document pixels.</summary>
	public SKPoint Position { get; set; }

	/// <summary>Current size of the element's bounding box in document pixels.</summary>
	public abstract SKSize Size { get; }

	public SKRect Bounds => SKRect.Create(Position, Size);

	/// <summary>Whether <paramref name="point"/> (in document pixels) lies inside the element.</summary>
	public virtual bool Contains(SKPoint point) => Bounds.Contains(point.X, point.Y);

	/// <summary>Draws the element onto the document.</summary>
	public abstract void Draw(SKCanvas canvas);

	/// <summary>
	/// State of an element captured when a scale drag starts, so every drag step can be
	/// applied relative to the element as it was when the drag began.
	/// </summary>
	public abstract record ScaleState;

	/// <summary>Captures the element's current state to pass to <see cref="ScaleTo"/> during a scale drag.</summary>
	public abstract ScaleState BeginScale();

	/// <summary>An immutable snapshot of an element's mutable state, captured by <see cref="CaptureState"/>.</summary>
	public abstract record ElementState;

	/// <summary>Captures the element's current state so <see cref="RestoreState"/> can put it back later (undo/redo).</summary>
	public abstract ElementState CaptureState();

	/// <summary>Restores a state previously captured by <see cref="CaptureState"/>.</summary>
	public abstract void RestoreState(ElementState state);

	/// <summary>
	/// Resizes the element so that <paramref name="anchor"/>, the corner opposite the grabbed
	/// handle, stays fixed while the drag cursor is at <paramref name="cursor"/>. Both points
	/// are in document pixels and <paramref name="state"/> is the snapshot from <see cref="BeginScale"/>.
	/// </summary>
	public abstract void ScaleTo(SKPoint cursor, SKPoint anchor, ScaleState state, PointerEventArgs pointerEvent);

	/// <summary>
	/// Adds this element's type-specific rows to the inspector: font and styling for labels,
	/// size and aspect ratio for images. Position and layering rows are handled by the panel.
	/// </summary>
	public abstract void BuildInspectorRows(IInspectorBuilder builder);
}
