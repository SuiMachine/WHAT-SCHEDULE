// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace WhatSchedule;

public static class ThemeDefinition
{
	/// <summary>The static definition of the element hierarchy view docked under the inspector: its metrics, its style and its pointer tuning.</summary>
	public static class HierarchyView
	{
		/// <summary>The font size of the header, the hint and the element names.</summary>
		public const double FontSize = 12;

		/// <summary>The height of the resize band at the top edge, in pixels.</summary>
		public const double GripHeight = 6;

		/// <summary>The height of the header band between the resize band and the list, in pixels.</summary>
		public const double HeaderHeight = 20;

		/// <summary>The distance of the first row from the top edge, in pixels.</summary>
		public const double ListTop = GripHeight + HeaderHeight;

		/// <summary>The height of one element row, in pixels.</summary>
		public const double RowHeight = 20;

		/// <summary>The horizontal padding of the drawn texts, in pixels.</summary>
		public const double TextPadding = 8;

		/// <summary>The indent of one hierarchy level, in pixels.</summary>
		public const double IndentWidth = 12;

		/// <summary>The size of the group rows' expansion chevron, in pixels; the shape spans it.</summary>
		public const double ChevronSize = 9;

		/// <summary>The chevron of a group row whose children are listed, matching the inspector's section arrows.</summary>
		public static readonly Geometry ExpandedChevron = Geometry.Parse("M0,2 L4.5,6 L9,2");

		/// <summary>The chevron of a group row whose children are hidden, pointing at the ones it would show.</summary>
		public static readonly Geometry CollapsedChevron = Geometry.Parse("M2,0 L6,4.5 L2,9");

		/// <summary>The pen of the group rows' expansion chevrons.</summary>
		public static readonly IPen ChevronPen = new Pen(RulerLabel, 1.5);

		/// <summary>The width of the new group button in the header band, in pixels.</summary>
		public const double NewGroupButtonWidth = 16;

		/// <summary>The height of the new group button in the header band, in pixels.</summary>
		public const double NewGroupButtonHeight = 14;

		/// <summary>The length of the arms of the new group button's plus sign, in pixels.</summary>
		public const double NewGroupPlusArm = 3;

		/// <summary>The smallest height the top edge can shrink the view to, in pixels.</summary>
		public const double MinViewHeight = 80;

		/// <summary>The height left to the inspector above when the view is grown the largest, in pixels.</summary>
		public const double MinSiblingHeight = 120;

		/// <summary>How far the pointer must move before a pressed row turns into a drag, in pixels.</summary>
		public const double DragThreshold = 3;

		/// <summary>The part of a row around its vertical center that drops the dragged element onto the row instead of between rows; narrow, so between-row drops dominate.</summary>
		public const double DropBand = 0.25;

		/// <summary>The highlight of the row of the element selected on the canvas.</summary>
		public static readonly ISolidColorBrush SelectedRowFill = new ImmutableSolidColorBrush(Selection.Color, 0.2);

		/// <summary>The highlight of the row under the pointer.</summary>
		public static readonly ISolidColorBrush HoverRowFill = new ImmutableSolidColorBrush(Colors.White, 0.13);

		/// <summary>The highlight of the row a dragged element would be dropped onto.</summary>
		public static readonly ISolidColorBrush DropRowFill = new ImmutableSolidColorBrush(Selection.Color, 0.35);

		/// <summary>The pen of the lines separating the bands and the rows.</summary>
		public static readonly IPen SeparatorPen = new Pen(Border, 1);

		/// <summary>The pen of the insertion line shown while a row is dragged.</summary>
		public static readonly IPen InsertionPen = new Pen(Selection, 1);

		/// <summary>The pen of the plus sign of the new group button in the header band.</summary>
		public static readonly IPen PlusPen = new Pen(RulerLabel, 1);

		/// <summary>The title of the header band.</summary>
		public static readonly FormattedText HeaderLabel = new("Elements", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, FontSize, RulerLabel);

		/// <summary>The hint shown while the document has no elements, e.g. before a schedule is loaded.</summary>
		public static readonly FormattedText EmptyHint = new("No elements.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, FontSize, RulerTick);

		/// <summary>The ellipsis drawn where an element name does not fit its row.</summary>
		public static readonly FormattedText Ellipsis = new("…", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, FontSize, RulerLabel);

		/// <summary>The typeface group rows are drawn with, so containers stand out from their contents.</summary>
		public static readonly Typeface GroupTypeface = new(LabelTypeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

		/// <summary>The cursor over the rows and the new group button, which act on a click.</summary>
		public static readonly Cursor HandCursor = new(StandardCursorType.Hand);

		/// <summary>The cursor over the resize band at the top edge.</summary>
		public static readonly Cursor ResizeCursor = new(StandardCursorType.SizeNorthSouth);
	}

	public static readonly ISolidColorBrush Background = new ImmutableSolidColorBrush(0xFF202020);
	public static readonly ISolidColorBrush Border = new ImmutableSolidColorBrush(0xFF3F3F46);
	public static readonly ISolidColorBrush RulerBackground = new ImmutableSolidColorBrush(0xFF272727);
	public static readonly ISolidColorBrush RulerTick = new ImmutableSolidColorBrush(0xFF999999);
	public static readonly ISolidColorBrush RulerLabel = new ImmutableSolidColorBrush(0xFFD4D4D4);
	public static readonly ISolidColorBrush Selection = new ImmutableSolidColorBrush(0xFF4CC2FF);
	public static readonly ISolidColorBrush SelectionLocked = new ImmutableSolidColorBrush(0xFFFF0000);
	public static readonly ISolidColorBrush HandleFill = new ImmutableSolidColorBrush(Colors.White);
	public static readonly ISolidColorBrush Guide = new ImmutableSolidColorBrush(0xFF00A2ED);
	public static readonly ISolidColorBrush AboutHeader = new ImmutableSolidColorBrush(0xFF4CC2FF);

	public static readonly IPen RulerBorderPen = new Pen(Border, 1);
	public static readonly IPen TickPen = new Pen(RulerTick, 1);
	public static readonly IPen SelectionPen = new Pen(Selection, 1);
	public static readonly IPen SelectionPenLocked = new Pen(SelectionLocked, 1);
	public static readonly IPen GuidePen = new Pen(Guide, 1) { DashStyle = DashStyle.Dash };
	/// <summary>Dashed like <see cref="GuidePen"/> to read as a preview rather than a real border.</summary>
	public static readonly IPen CropBoundsPen = new Pen(Selection, 1) { DashStyle = DashStyle.Dash };
	public static readonly Typeface LabelTypeface = Typeface.Default;

	public const double RulerThickness = 22;
	public const double MinZoom = 0.02;
	public const double MaxZoom = 32;
	public const double WheelFactor = 1.1;
	public const double MinVisibleEdge = 40;
	public const double MinMajorTickSpacing = 60;
	public const double LabelFontSize = 10;
	public const double HandleSize = 8;
	public const double HandleHitRadius = 6;
	public const double RotateHandleOffset = 20;
	public const double RotationSnap = 15;
	public const double GuideHitTolerance = 2;
	/// <summary>Within this radius around the press point a held left button counts as a click, not a move drag, so an accidental nudge while selecting cannot shift the element.</summary>
	public const double MoveStartRadius = 3;
	public const double MinElementSize = 8;
	public const float MinFontSize = 4;
}
