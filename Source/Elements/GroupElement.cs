using Avalonia.Input;
using SkiaSharp;
using WhatSchedule.Controls;

namespace WhatSchedule.Elements;

/// <summary>
/// A container of other elements transformed together: the children live in the group's own
/// content space, and the group places that space into its parent with its local position,
/// rotation and scale. The group's bounds enclose its children, and its rotation and scale
/// pivot around the content origin, so moving one child never shifts its siblings.
/// </summary>
public sealed class GroupElement : CanvasElement
{
	/// <summary>The smallest scale a group can be dragged down to, so it can never collapse to nothing.</summary>
	private const float MinScale = 0.05f;

	private readonly List<CanvasElement> children = new();

	/// <summary>The group's kind as shown to the user, e.g. in the undo history.</summary>
	public override string Kind => GroupElementData.KindName;

	/// <summary>The elements in the group, in rendering order, bottom first.</summary>
	public IReadOnlyList<CanvasElement> Children => children;

	/// <summary>Horizontal scale of the group's content, 1 being the children's natural size.</summary>
	public float ScaleX { get; set; } = 1f;

	/// <summary>Vertical scale of the group's content, 1 being the children's natural size.</summary>
	public float ScaleY { get; set; } = 1f;

	/// <summary>The group's extent in its own content space, i.e. the union of the children's boxes.</summary>
	public override SKSize Size => ContentBounds.Size;

	/// <summary>The union of the children's boxes mapped into the group's content space; the group's frame always encloses what it shows.</summary>
	public override SKRect ContentBounds
	{
		get
		{
			if (children.Count == 0)
				return SKRect.Create(0, 0, 0, 0);

			var result = default(SKRect);
			for (var i = 0; i < children.Count; i++)
			{
				var box = children[i].ContentBounds;
				var corners = new[]
				{
					children[i].ToParentSpace(new SKPoint(box.Left, box.Top)),
					children[i].ToParentSpace(new SKPoint(box.Right, box.Top)),
					children[i].ToParentSpace(new SKPoint(box.Right, box.Bottom)),
					children[i].ToParentSpace(new SKPoint(box.Left, box.Bottom)),
				};
				var left = Math.Min(Math.Min(corners[0].X, corners[1].X), Math.Min(corners[2].X, corners[3].X));
				var top = Math.Min(Math.Min(corners[0].Y, corners[1].Y), Math.Min(corners[2].Y, corners[3].Y));
				var right = Math.Max(Math.Max(corners[0].X, corners[1].X), Math.Max(corners[2].X, corners[3].X));
				var bottom = Math.Max(Math.Max(corners[0].Y, corners[1].Y), Math.Max(corners[2].Y, corners[3].Y));
				var childBounds = SKRect.Create(left, top, right - left, bottom - top);
				result = i == 0 ? childBounds : new SKRect(
					Math.Min(result.Left, childBounds.Left),
					Math.Min(result.Top, childBounds.Top),
					Math.Max(result.Right, childBounds.Right),
					Math.Max(result.Bottom, childBounds.Bottom));
			}

			return result;
		}
	}

	/// <summary>Groups rotate around their content origin, which <see cref="RebaseToContentBounds"/> keeps at the center of the bounds.</summary>
	protected override SKPoint Pivot => SKPoint.Empty;

	/// <summary>Places the group's content space into its parent: the origin moves to the local position, then the content rotates and scales around it.</summary>
	public override SKPoint ToParentSpace(SKPoint point) =>
		Position + RotateVector(new SKPoint(point.X * ScaleX, point.Y * ScaleY), Rotation);

	/// <summary>The inverse of <see cref="ToParentSpace"/>.</summary>
	public override SKPoint ToContentSpace(SKPoint point)
	{
		var rotated = RotateVector(point - Position, -Rotation);
		return new SKPoint(ScaleX != 0 ? rotated.X / ScaleX : rotated.X, ScaleY != 0 ? rotated.Y / ScaleY : rotated.Y);
	}

	/// <summary>Draws the children inside the group's transform; the canvas state is the parent's content space.</summary>
	public override void Draw(SKCanvas canvas)
	{
		var count = canvas.Save();
		canvas.Translate(Position.X, Position.Y);
		canvas.RotateDegrees(Rotation);
		canvas.Scale(ScaleX, ScaleY);
		foreach (var child in children)
			child.Draw(canvas);
		canvas.RestoreToCount(count);
	}

	public override ScaleState BeginScale() => new GroupScaleState(ContentBounds, Position, Rotation, ScaleX, ScaleY);

	/// <summary>Scales the group's content: the content space grows, so the children keep their coordinates and render larger.</summary>
	internal override void BakeScale(SKPoint factor)
	{
		ScaleX *= factor.X;
		ScaleY *= factor.Y;
	}

	/// <summary>Groups cannot scale from a content-space cursor; the scale drag drives <see cref="ScaleToParent"/> instead.</summary>
	public override void ScaleTo(SKPoint cursor, SKPoint anchor, ScaleState state, PointerEventArgs pointerEvent) =>
		throw new NotSupportedException("Groups are scaled through ScaleToParent.");

	/// <summary>
	/// Scales the group so the grabbed corner follows <paramref name="cursor"/> (in the parent's content space)
	/// while the opposite corner, captured in <paramref name="start"/>, stays pinned in place. The aspect ratio
	/// is always preserved: both axes scale by the larger factor, since a group's content is a composition.
	/// </summary>
	internal void ScaleToParent(SKPoint cursor, Corner corner, GroupScaleState start)
	{
		var anchor = SelectionLayer.GetAnchor(start.Bounds, corner);
		var grabbed = SelectionLayer.GetCorner(start.Bounds, corner);
		var anchorParent = start.Position + RotateVector(new SKPoint(anchor.X * start.ScaleX, anchor.Y * start.ScaleY), start.Rotation);

		//The cursor is brought into the group's unrotated axes so each axis scales independently of the group's rotation.
		var offset = RotateVector(cursor - anchorParent, -start.Rotation);
		var scaleX = grabbed.X != anchor.X ? Math.Abs(offset.X / (grabbed.X - anchor.X)) : start.ScaleX;
		var scaleY = grabbed.Y != anchor.Y ? Math.Abs(offset.Y / (grabbed.Y - anchor.Y)) : start.ScaleY;
		scaleX = scaleY = Math.Max(scaleX, scaleY);

		ScaleX = Math.Max(MinScale, scaleX);
		ScaleY = Math.Max(MinScale, scaleY);

		//The pinned anchor decides where the content origin lands in the parent's space.
		Position = anchorParent - RotateVector(new SKPoint(anchor.X * ScaleX, anchor.Y * ScaleY), Rotation);
	}

	/// <summary>
	/// Shifts the whole content so the bounding box is centered on the content origin and moves <see cref="Position"/>
	/// through the group's own transform, so nothing moves on screen; returns the applied content-space shift,
	/// which is empty when the box is already centered. Rotation pivots around the content origin, so this
	/// re-anchors it to the center of the box whenever the content grows or shrinks.
	/// </summary>
	internal SKPoint RebaseToContentBounds()
	{
		var bounds = ContentBounds;
		var delta = new SKPoint(bounds.MidX, bounds.MidY);
		if (delta.X == 0 && delta.Y == 0)
			return SKPoint.Empty;

		foreach (var child in children)
			child.Position -= delta;

		Position += RotateVector(new SKPoint(delta.X * ScaleX, delta.Y * ScaleY), Rotation);
		return delta;
	}

	/// <summary>Puts <paramref name="child"/> into the group at <paramref name="index"/> of the rendering order, bottom first.</summary>
	internal void InsertChild(CanvasElement child, int index)
	{
		children.Insert(index, child);
		child.Parent = this;
	}

	/// <summary>Takes <paramref name="child"/> out of the group.</summary>
	internal void RemoveChild(CanvasElement child)
	{
		children.Remove(child);
		child.Parent = null;
	}

	/// <summary>The index of <paramref name="child"/> in the group, or -1 when it is not in it.</summary>
	internal int IndexOfChild(CanvasElement child) => children.IndexOf(child);

	public override ElementState CaptureState() => new GroupElementState(Position, Rotation, ScaleX, ScaleY);

	public override void RestoreState(ElementState state)
	{
		if (state is not GroupElementState snapshot)
			return;

		Position = snapshot.Position;
		Rotation = snapshot.Rotation;
		ScaleX = snapshot.ScaleX;
		ScaleY = snapshot.ScaleY;
	}

	/// <summary>Creates a deep copy of the group by restoring its own state and cloning every child into it.</summary>
	public override CanvasElement Clone()
	{
		var clone = new GroupElement();
		clone.RestoreState(CaptureState());
		clone.Tag = Tag;
		foreach (var child in children)
			clone.InsertChild(child.Clone(), clone.children.Count);

		return clone;
	}

	/// <summary>The group's own rows: the scale factors of its content. Position and layering are handled by the panel.</summary>
	public override void BuildInspectorRows(IInspectorBuilder builder)
	{
		builder.AddSection("Scale");
		builder.AddNumber("Scale X", () => ScaleX, value => ScaleX = Math.Max(MinScale, value));
		builder.AddNumber("Scale Y", () => ScaleY, value => ScaleY = Math.Max(MinScale, value));
	}

	/// <summary>The group's frame and transform captured when a scale drag starts, so every drag step is applied relative to the group as it was.</summary>
	public sealed record GroupScaleState(SKRect Bounds, SKPoint Position, float Rotation, float ScaleX, float ScaleY) : ScaleState;

	public sealed record GroupElementState(SKPoint Position, float Rotation, float ScaleX, float ScaleY) : ElementState;

	public sealed class GroupElementData : ElementData
	{
		public const string KindName = "Group";

		public override string Kind => KindName;

		public float ScaleX { get; set; } = 1f;

		public float ScaleY { get; set; } = 1f;

		/// <summary>The group's child elements, nested recursively, in rendering order bottom first.</summary>
		public List<ElementData> Children { get; set; } = new();
	}
}
