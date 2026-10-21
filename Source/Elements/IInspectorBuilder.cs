using Avalonia.Controls;
using SkiaSharp;

namespace WhatSchedule.Elements;

/// <summary>
/// The editor operations the inspector panel offers to an element describing its own
/// rows in <see cref="CanvasElement.BuildInspectorRows"/>: typed editors that commit
/// through the document's undo history, plus raw rows for anything custom.
/// </summary>
public interface IInspectorBuilder
{
	/// <summary>Adds a collapsible section header above the rows that follow it; collapsed sections hide their rows until expanded again.</summary>
	void AddSection(string title);

	/// <summary>Adds a custom editor, or any other control, in the right-hand column.</summary>
	void AddRow(string header, Control editor);

	/// <summary>A text editor that commits on Enter or when it loses focus; empty input is discarded.</summary>
	void AddTextBox(string header, Func<string> getter, Action<string> setter);

	/// <summary>A multi-line text editor that commits when it loses focus; Enter adds a line break and empty input is discarded.</summary>
	void AddTextArea(string header, Func<string> getter, Action<string> setter);

	/// <summary>A combo box of the given items that commits as soon as the selection changes.</summary>
	void AddComboBox(string header, string[] items, Func<string> getter, Action<string> setter);

	/// <summary>A numeric editor with spin buttons that previews the value live while it changes and
	/// commits a whole edit run as a single undo entry; invalid input is discarded, and the value is
	/// clamped to <paramref name="minimum"/> and <paramref name="maximum"/> when they are given.</summary>
	void AddNumber(string header, Func<float> getter, Action<float> setter, float? minimum = null, float? maximum = null);

	/// <summary>A switch that commits as soon as the user toggles it.</summary>
	void AddToggle(string header, Func<bool> getter, Action<bool> setter);

	/// <summary>A slider that previews the value live while dragged and commits the whole drag
	/// as a single undo entry; keyboard steps commit each on their own.</summary>
	void AddSlider(string header, float minimum, float maximum, Func<float> getter, Action<float> setter);

	/// <summary>A hex color editor (#RRGGBB or #RRGGBBAA) with a live swatch of the current color.</summary>
	void AddColor(string header, Func<SKColor> getter, Action<SKColor> setter);

	/// <summary>A combo box of the installed font families, with the element's family preselected.</summary>
	void AddFontBox(string header, Func<string> getter, Action<string> setter);

	/// <summary>
	/// Re-runs <paramref name="sync"/> whenever the element changes on the canvas
	/// (dragging, scaling, undo/redo), so the row can follow values it does not edit.
	/// </summary>
	void AddSync(Action sync);

	/// <summary>Applies <paramref name="mutation"/> to the selected element and records it in the undo
	/// history as <paramref name="description"/>, which the undo and redo commands then show.</summary>
	void Apply(string description, Action mutation);
}
