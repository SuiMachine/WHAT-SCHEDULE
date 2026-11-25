// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

namespace WhatSchedule.Documents;

/// <summary>
/// A text layer's character style, taken from the first style run of its engine data:
/// the PostScript name of the run's font, its size in points (pixels at the document's 72 ppi), faux bold and italic,
/// the fill color with its alpha, letter spacing in thousandths of an em, horizontal stretch as a fraction of the
/// natural width, and the line spacing in points when auto leading is off.
/// </summary>
public sealed record PsdTextStyle(string? FontName, float? FontSize, bool Bold, bool Italic, PsdColor? FillColor, byte FillAlpha, float? Tracking, float? HorizontalScale, float? Leading, bool AutoLeading);

/// <summary>
/// A text layer's content and placement, read from its type tool data ('TySh'/'tySh'):
/// the text with carriage returns for line breaks, the text box's bounds on the canvas (empty for point text),
/// whether the text runs vertically, the paragraph justification as Photoshop numbers it (0 left, 1 right, 2 center),
/// and the first style run.
/// </summary>
public sealed record PsdText(string Text, PsdRect Box, bool Vertical, int Justification, PsdTextStyle? Style);
