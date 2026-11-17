// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Avalonia.Input;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>The cursors used by the preview canvas interactions.</summary>
internal static class CanvasCursors
{
	private static readonly Cursor TopLeft = new(StandardCursorType.TopLeftCorner);
	private static readonly Cursor TopRight = new(StandardCursorType.TopRightCorner);
	private static readonly Cursor BottomRight = new(StandardCursorType.BottomRightCorner);
	private static readonly Cursor BottomLeft = new(StandardCursorType.BottomLeftCorner);

	public static readonly Cursor Move = new(StandardCursorType.SizeAll);
	public static readonly Cursor Rotate = new(StandardCursorType.Cross);
	/// <summary>The cursor while a click on the canvas samples its color; a crosshair for the pixel it will pick.</summary>
	public static readonly Cursor Sample = new(StandardCursorType.Cross);
	private static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
	private static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
	public static readonly Cursor HorizontalGuide = new(StandardCursorType.SizeNorthSouth);
	public static readonly Cursor VerticalGuide = new(StandardCursorType.SizeWestEast);

	public static Cursor ForCorner(Corner corner) => corner switch
	{
		Corner.TopLeft => TopLeft,
		Corner.TopRight => TopRight,
		Corner.BottomRight => BottomRight,
		_ => BottomLeft,
	};

	public static Cursor ForEdge(Edge edge) => edge is Edge.Left or Edge.Right ? SizeWestEast : SizeNorthSouth;
}
