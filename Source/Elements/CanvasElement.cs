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
	/// <summary>The group this element lives in, or null when it sits at the document root.</summary>
	public GroupElement? Parent { get; internal set; }

	/// <summary>The position of the element's top-left corner in the parent's content space, before rotation; document space at the root.</summary>
	public SKPoint Position { get; set; }

	/// <summary>Local rotation of the element, duh! In degrees, clockwise, around the rotation pivot.</summary>
	public float Rotation { get; set; }

	/// <summary>Current size of the element's bounding box in content pixels.</summary>
	public abstract SKSize Size { get; }

	/// <summary>The element's kind as shown to the user, e.g. in the undo history.</summary>
	public abstract string Kind { get; }

	/// <summary>The user-given name shown in the hierarchy list, or null to fall back to <see cref="Kind"/>.</summary>
	public string? Name { get; set; }

	/// <summary>The element's name as shown in the hierarchy list; the user-given name, the kind by default.</summary>
	public virtual string DisplayName => Name ?? Kind;

	/// <summary>The unrotated box of the element in its own content space: leaves place their box at <see cref="Position"/>, groups enclose their children.</summary>
	public virtual SKRect ContentBounds => SKRect.Create(Position, Size);

	/// <summary>The element's unrotated frame in the parent's content space, i.e. where the element's own rotation is applied around.</summary>
	public SKRect Bounds => SKRect.Create(Position, Size);

	/// <summary>The point the element rotates around, in its own content space; groups pivot around their content origin.</summary>
	protected virtual SKPoint Pivot => new(ContentBounds.MidX, ContentBounds.MidY);

	/// <summary>The rotation pivot mapped into the parent's content space: the anchor that carries the element's transform, which reparenting converts so a rotated parent chain cannot shift the element.</summary>
	internal SKPoint PivotInParent => ToParentSpace(Pivot);

	/// <summary>The product of the ancestor chain's scale factors starting at <paramref name="parent"/>, per axis.</summary>
	internal static SKPoint AncestorScale(GroupElement? parent)
	{
		var scale = new SKPoint(1f, 1f);
		for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
			scale = new SKPoint(scale.X * ancestor.ScaleX, scale.Y * ancestor.ScaleY);

		return scale;
	}

	/// <summary>
	/// Multiplies the element's own extent by the per-axis <paramref name="factor"/>: font and box for labels,
	/// frame for images, content scale for groups. Reparenting bakes the difference between the old and the
	/// new parent chain's scales this way, so the element keeps its rendered size when it changes containers.
	/// </summary>
	internal virtual void BakeScale(SKPoint factor) { }

	/// <summary>Maps <paramref name="point"/> from the element's content space into the parent's content space, applying only the element's own rotation (and scale for groups).</summary>
	public virtual SKPoint ToParentSpace(SKPoint point) => Rotate(point, Pivot, Rotation);

	/// <summary>Maps <paramref name="point"/> from the parent's content space into the element's content space; the inverse of <see cref="ToParentSpace"/>.</summary>
	public virtual SKPoint ToContentSpace(SKPoint point) => Rotate(point, Pivot, -Rotation);

	/// <summary>Maps <paramref name="point"/> from the parent's content space into document space, through every ancestor's transform.</summary>
	public SKPoint ParentToDocument(SKPoint point) => AncestorToDocument(Parent, point);

	/// <summary>Maps <paramref name="point"/> from document space into the parent's content space, undoing every ancestor's transform.</summary>
	public SKPoint DocumentToParent(SKPoint point) => AncestorFromDocument(Parent, point);

	/// <summary>The total rotation of every ancestor group, in degrees; the element's visual rotation is this plus <see cref="Rotation"/>.</summary>
	public float ParentRotation => AncestorRotation(Parent);

	/// <summary>Transforms <paramref name="point"/> from document space into the element's unrotated local space.</summary>
	public SKPoint ToLocal(SKPoint point) => ToContentSpace(DocumentToParent(point));

	/// <summary>Transforms <paramref name="point"/> from the element's unrotated local space into document space.</summary>
	public SKPoint ToDocument(SKPoint point) => ParentToDocument(ToParentSpace(point));

	/// <summary>The corners of the element's visual bounds in document space, i.e. its content box through its own and every ancestor's transform.</summary>
	public SKPoint[] DocumentCorners()
	{
		var bounds = ContentBounds;
		return new[]
		{
			ToDocument(new SKPoint(bounds.Left, bounds.Top)),
			ToDocument(new SKPoint(bounds.Right, bounds.Top)),
			ToDocument(new SKPoint(bounds.Right, bounds.Bottom)),
			ToDocument(new SKPoint(bounds.Left, bounds.Bottom)),
		};
	}

	/// <summary>The smallest document-space rectangle enclosing the element's visual bounds.</summary>
	public SKRect DocumentBounds
	{
		get
		{
			var corners = DocumentCorners();
			var left = corners[0].X;
			var top = corners[0].Y;
			var right = corners[0].X;
			var bottom = corners[0].Y;
			for (var i = 1; i < corners.Length; i++)
			{
				left = Math.Min(left, corners[i].X);
				top = Math.Min(top, corners[i].Y);
				right = Math.Max(right, corners[i].X);
				bottom = Math.Max(bottom, corners[i].Y);
			}

			return SKRect.Create(left, top, right - left, bottom - top);
		}
	}

	/// <summary>The point the element rotates around, in document space.</summary>
	public SKPoint DocumentPivot => ToDocument(Pivot);

	/// <summary>Maps <paramref name="point"/> from the content space of <paramref name="parent"/> into document space through the whole ancestor chain.</summary>
	internal static SKPoint AncestorToDocument(GroupElement? parent, SKPoint point)
	{
		for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
			point = ancestor.ToParentSpace(point);

		return point;
	}

	/// <summary>Maps <paramref name="point"/> from document space into the content space of <paramref name="parent"/>, undoing the whole ancestor chain.</summary>
	internal static SKPoint AncestorFromDocument(GroupElement? parent, SKPoint point)
	{
		var chain = new List<GroupElement>();
		for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
			chain.Add(ancestor);

		for (var i = chain.Count - 1; i >= 0; i--)
			point = chain[i].ToContentSpace(point);

		return point;
	}

	/// <summary>The total rotation of the ancestor chain starting at <paramref name="parent"/>, in degrees.</summary>
	internal static float AncestorRotation(GroupElement? parent)
	{
		var sum = 0f;
		for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
			sum += ancestor.Rotation;

		return sum;
	}

	/// <summary>Rotates <paramref name="point"/> around <paramref name="center"/> by <paramref name="degrees"/> (clockwise, y-down).</summary>
	internal static SKPoint Rotate(SKPoint point, SKPoint center, float degrees) => center + RotateVector(point - center, degrees);

	/// <summary>Rotates <paramref name="vector"/> by <paramref name="degrees"/> (clockwise, y-down) around the origin.</summary>
	internal static SKPoint RotateVector(SKPoint vector, float degrees)
	{
		if (degrees == 0)
			return vector;

		var radians = (float)(degrees * Math.PI / 180);
		var cos = (float)Math.Cos(radians);
		var sin = (float)Math.Sin(radians);
		return new SKPoint(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
	}

	/// <summary>Whether <paramref name="point"/> (in document pixels) lies inside the rotated element.</summary>
	public virtual bool Contains(SKPoint point)
	{
		var local = ToLocal(point);
		return ContentBounds.Contains(local.X, local.Y);
	}

	/// <summary>Draws the element onto the document; the canvas state is the element's parent content space.</summary>
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
	/// are in the element's content space and <paramref name="state"/> is the snapshot from <see cref="BeginScale"/>.
	/// Groups scale through <see cref="GroupElement.ScaleToParent"/> instead, because their live scale
	/// would feed back into the content-space cursor.
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
	/// element's local position and rotation. Groups nest their children recursively.
	/// </summary>
	public abstract class ElementData
	{
		/// <summary>The discriminator naming the element type this shape stores.</summary>
		public abstract string Kind { get; }

		/// <summary>The user-given hierarchy name, or null when the element shows its kind.</summary>
		public string? Name { get; set; }

		/// <summary>The element's local position in its parent's content space.</summary>
		public PointData Position { get; set; } = new();

		/// <summary>The element's local rotation in degrees, clockwise.</summary>
		public float Rotation { get; set; }
	}

	/// <summary>JSON shape of a document-pixel coordinate pair.</summary>
	public sealed class PointData
	{
		public float X { get; set; }

		public float Y { get; set; }
	}
}
