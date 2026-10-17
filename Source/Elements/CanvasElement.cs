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
	/// <summary>The position of the element's top-left corner in document pixels, before rotation.</summary>
	public SKPoint Position { get; set; }
	/// <summary>Rotation of an element, duh! In degrees, clockwise, around the element's center.</summary>
	public float Rotation { get; set; }

	/// <summary>Current size of the element's bounding box in document pixels.</summary>
	public abstract SKSize Size { get; }

	/// <summary>The center the element rotates around, in document pixels.</summary>
	public SKPoint Center => new(Position.X + Size.Width / 2, Position.Y + Size.Height / 2);

	public SKRect Bounds => SKRect.Create(Position, Size);

	/// <summary>Transforms <paramref name="point"/> from document space into the element's unrotated local space.</summary>
	public SKPoint ToLocal(SKPoint point) => Rotate(point, Center, -Rotation);

	/// <summary>Transforms <paramref name="point"/> from the element's unrotated local space into document space.</summary>
	public SKPoint ToDocument(SKPoint point) => Rotate(point, Center, Rotation);

	/// <summary>Rotates <paramref name="point"/> around <paramref name="center"/> by <paramref name="degrees"/> (clockwise, y-down).</summary>
	private static SKPoint Rotate(SKPoint point, SKPoint center, float degrees)
	{
		if (degrees == 0)
			return point;

		var radians = (float)(degrees * Math.PI / 180);
		var cos = (float)Math.Cos(radians);
		var sin = (float)Math.Sin(radians);
		var x = point.X - center.X;
		var y = point.Y - center.Y;
		return new SKPoint(center.X + x * cos - y * sin, center.Y + x * sin + y * cos);
	}

	/// <summary>Whether <paramref name="point"/> (in document pixels) lies inside the rotated element.</summary>
	public virtual bool Contains(SKPoint point)
	{
		var local = ToLocal(point);
		return Bounds.Contains(local.X, local.Y);
	}

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

	/// <summary>Creates a deep copy of the element with the same state, e.g. for the copy/paste clipboard.</summary>
	public abstract CanvasElement Clone();

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

	/// <summary>
	/// JSON shape of a canvas element saved in a document: <see cref="Kind"/> picks the concrete
	/// storage class nested in the element subclasses when loading, and every shape carries the
	/// element's position and rotation.
	/// </summary>
	public abstract class ElementData
	{
		/// <summary>The discriminator naming the element type this shape stores.</summary>
		public abstract string Kind { get; }

		public PointData? Position { get; set; }

		/// <summary>The element's rotation in degrees, clockwise; null in documents saved before rotation existed.</summary>
		public float? Rotation { get; set; }
	}

	/// <summary>JSON shape of a document-pixel coordinate pair.</summary>
	public sealed class PointData
	{
		public float X { get; set; }

		public float Y { get; set; }
	}
}
