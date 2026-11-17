// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>
/// Draws the vector mask of the group being edited on the canvas — the polygon through the group's own
/// transform with a handle on each vertex — and finds where the pointer grabs the mask: at a vertex,
/// on an edge, or inside the loop.
/// </summary>
internal static class MaskLayer
{
	/// <summary>The mask vertex <paramref name="position"/> is over, within the handle hit radius.</summary>
	public static int? HitTestVertex(Point position, GroupElement group, Rect imageRect, double scale)
	{
		if (group.Mask is not { } vertices)
			return null;

		for (var i = 0; i < vertices.Count; i++)
		{
			var screen = ToScreen(group.ToDocument(vertices[i]), imageRect, scale);
			if (Math.Abs(position.X - screen.X) <= ThemeDefinition.HandleHitRadius &&
				Math.Abs(position.Y - screen.Y) <= ThemeDefinition.HandleHitRadius)
				return i;
		}

		return null;
	}

	/// <summary>The mask edge <paramref name="position"/> is over, within the hit radius, naming the index of the vertex the edge starts at; the closing edge starts at the last vertex.</summary>
	public static int? HitTestEdge(Point position, GroupElement group, Rect imageRect, double scale)
	{
		if (group.Mask is not { Count: >= 2 } vertices)
			return null;

		var best = (int?)null;
		var bestDistance = ThemeDefinition.HandleHitRadius;
		for (var i = 0; i < vertices.Count; i++)
		{
			var a = ToScreen(group.ToDocument(vertices[i]), imageRect, scale);
			var b = ToScreen(group.ToDocument(vertices[(i + 1) % vertices.Count]), imageRect, scale);
			var current = DistanceToSegment(position, a, b);
			if (current <= bestDistance)
			{
				best = i;
				bestDistance = current;
			}
		}

		return best;
	}

	/// <summary>Puts a new mask vertex onto the edge starting at <paramref name="edge"/> — the edge from vertex
	/// <paramref name="edge"/> to the one after it — at <paramref name="point"/> in the group's content space,
	/// and returns the new vertex's index.</summary>
	public static int InsertVertex(GroupElement group, int edge, SKPoint point)
	{
		var vertices = group.Mask ??= new List<SKPoint>();
		vertices.Insert(edge + 1, point);
		return edge + 1;
	}

	/// <summary>Takes the mask vertex at <paramref name="index"/> away; the loop closes over the gap the two neighbouring edges leave.</summary>
	public static void RemoveVertex(GroupElement group, int index)
	{
		group.Mask?.RemoveAt(index);
	}

	/// <summary>Whether <paramref name="cursor"/> (in document pixels) lies inside the mask polygon, i.e. grabs the mask for a move instead of the elements underneath.</summary>
	public static bool HitTestInside(SKPoint cursor, GroupElement group)
	{
		if (group.Mask is not { Count: >= 3 } vertices)
			return false;

		//Ray casting: a point is inside when a horizontal ray from it crosses the loop's edges an odd number of times.
		var local = group.ToLocal(cursor);
		var inside = false;
		for (var i = 0; i < vertices.Count; i++)
		{
			var a = vertices[i];
			var b = vertices[(i + 1) % vertices.Count];
			if ((a.Y > local.Y) != (b.Y > local.Y) &&
				local.X < (b.X - a.X) * (local.Y - a.Y) / (b.Y - a.Y) + a.X)
				inside = !inside;
		}

		return inside;
	}

	/// <summary>Draws the mask polygon through the group's own transform, with a handle on each vertex like the selection layer draws for the selection.</summary>
	public static void Draw(DrawingContext context, GroupElement group, Rect imageRect, double scale)
	{
		if (group.Mask is not { Count: >= 2 } vertices)
			return;

		var box = new StreamGeometry();
		using (var ctx = box.Open())
		{
			ctx.BeginFigure(ToScreen(group.ToDocument(vertices[0]), imageRect, scale), true);
			for (var i = 1; i < vertices.Count; i++)
				ctx.LineTo(ToScreen(group.ToDocument(vertices[i]), imageRect, scale));
			ctx.EndFigure(true);
		}
		context.DrawGeometry(null, ThemeDefinition.SelectionPen, box);

		var half = ThemeDefinition.HandleSize / 2;
		for (var i = 0; i < vertices.Count; i++)
		{
			var screen = ToScreen(group.ToDocument(vertices[i]), imageRect, scale);
			var handle = new Rect(screen.X - half, screen.Y - half, ThemeDefinition.HandleSize, ThemeDefinition.HandleSize);
			context.FillRectangle(ThemeDefinition.HandleFill, handle);
			context.DrawRectangle(ThemeDefinition.SelectionPen, handle);
		}
	}

	/// <summary>The shortest distance from <paramref name="point"/> to the segment <paramref name="a"/>-<paramref name="b"/>.</summary>
	private static double DistanceToSegment(Point point, Point a, Point b)
	{
		var dx = b.X - a.X;
		var dy = b.Y - a.Y;
		var lengthSquared = dx * dx + dy * dy;
		var t = lengthSquared > 0 ? Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1) : 0;
		var x = a.X + t * dx;
		var y = a.Y + t * dy;
		return Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
	}

	private static Point ToScreen(SKPoint point, Rect imageRect, double scale) => new Point(imageRect.X + point.X * scale, imageRect.Y + point.Y * scale);
}
