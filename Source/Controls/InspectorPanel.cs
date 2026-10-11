using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SkiaSharp;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls;

/// <summary>
/// The property panel of the element selected on the canvas: position for every element,
/// font and styling for labels, size and aspect ratio for images. Every edit is applied
/// through the document's undo history, and the displayed values follow changes made on
/// the canvas (dragging, scaling, undo/redo) live.
/// </summary>
public sealed class InspectorPanel : StackPanel, IInspectorBuilder
{
	private static string[]? fontFamilies;

	private readonly List<Action> syncCallbacks = new();
	private PreviewCanvas? canvas;
	private CanvasElement? element;
	private Grid rows = null!;

	/// <summary>The font families installed on the machine, cached for the font combo boxes.</summary>
	private static string[] FontFamilies => fontFamilies ??= SKFontManager.Default.GetFontFamilies()
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
		.ToArray();

	/// <summary>Connects the inspector to a canvas and its selection and document changes.</summary>
	public void Bind(PreviewCanvas owner)
	{
		if (canvas is not null)
			throw new InvalidOperationException("The inspector is already bound to a canvas.");

		canvas = owner;
		canvas.SelectionChanged += () => Rebuild(canvas.Selection.SelectedElement);
		canvas.DocumentChanged += Sync;
		Rebuild(canvas.Selection.SelectedElement);
	}

	/// <summary>Rebuilds the panel for the selected element, or shows a hint when nothing is selected.</summary>
	private void Rebuild(CanvasElement? selected)
	{
		element = selected;
		syncCallbacks.Clear();
		Children.Clear();

		if (element is null)
		{
			Children.Add(new TextBlock { Text = "Nothing selected.", Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) });
			return;
		}

		Children.Add(new TextBlock
		{
			Text = element is LabelElement ? "Label" : "Image",
			FontWeight = FontWeight.Bold,
			Margin = new Thickness(0, 0, 0, 8),
		});

		rows = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*"),
			Margin = new Thickness(0, 0, 8, 0),
		};
		Children.Add(rows);

		AddSection("Position");
		AddNumber("X", () => element.Position.X, value => element.Position = new SKPoint(value, element.Position.Y));
		AddNumber("Y", () => element.Position.Y, value => element.Position = new SKPoint(element.Position.X, value));

		AddSection("Layer");
		AddLayerRow(element);

		element.BuildInspectorRows(this);
	}

	/// <summary>The <see cref="IInspectorBuilder"/> operations the selected element builds its rows with.</summary>
	void IInspectorBuilder.AddSection(string title) => AddSection(title);
	void IInspectorBuilder.AddRow(string header, Control editor) => AddRow(header, editor);
	void IInspectorBuilder.AddTextBox(string header, Func<string> getter, Action<string> setter) => AddTextBox(header, getter, setter);
	void IInspectorBuilder.AddNumber(string header, Func<float> getter, Action<float> setter) => AddNumber(header, getter, setter);
	void IInspectorBuilder.AddColor(string header, Func<SKColor> getter, Action<SKColor> setter) => AddColor(header, getter, setter);
	void IInspectorBuilder.AddFontBox(string header, Func<string> getter, Action<string> setter) => AddFontBox(header, getter, setter);
	void IInspectorBuilder.AddSync(Action sync) => syncCallbacks.Add(sync);
	void IInspectorBuilder.Apply(Action mutation) => Apply(mutation);

	/// <summary>The bring-forward/send-backward buttons, enabled per the element's position in the layering order.</summary>
	private void AddLayerRow(CanvasElement element)
	{
		var backward = new Button { Content = "Backward", Margin = new Thickness(0, 2, 4, 2) };
		var forward = new Button { Content = "Forward", Margin = new Thickness(4, 2, 0, 2) };

		backward.Click += (_, _) => canvas?.Document?.ReorderTracked(element, -1);
		forward.Click += (_, _) => canvas?.Document?.ReorderTracked(element, 1);

		var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
		Grid.SetColumn(backward, 0);
		Grid.SetColumn(forward, 1);
		row.Children.Add(backward);
		row.Children.Add(forward);

		void RefreshLayering()
		{
			var elements = canvas?.Document?.Elements;
			var index = IndexOf(element, elements);
			backward.IsEnabled = index > 0;
			forward.IsEnabled = index >= 0 && index < elements!.Count - 1;
		}

		RefreshLayering();
		AddRow(string.Empty, row);
		syncCallbacks.Add(RefreshLayering);
	}

	/// <summary>The index of <paramref name="element"/> in <paramref name="elements"/>, or -1 when it is not there.</summary>
	private static int IndexOf(CanvasElement element, IReadOnlyList<CanvasElement>? elements)
	{
		if (elements is null)
			return -1;

		for (var i = 0; i < elements.Count; i++)
		{
			if (ReferenceEquals(elements[i], element))
				return i;
		}

		return -1;
	}

	/// <summary>Applies <paramref name="mutation"/> to the selected element and records it in the undo history.</summary>
	private void Apply(Action mutation)
	{
		var document = canvas?.Document;
		if (element is null || document is null)
			return;

		var before = element.CaptureState();
		mutation();
		document.RecordStateChange(element, before);
		document.RaiseChanged();
	}

	/// <summary>Pushes the values of the selected element into the editors, skipping the ones being edited.</summary>
	private void Sync()
	{
		if (element is not null)
		{
			foreach (var sync in syncCallbacks)
				sync();
		}
	}

	private void AddSection(string title)
	{
		rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0, GridUnitType.Auto) });
		var row = rows.RowDefinitions.Count - 1;
		var header = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 2) };
		Grid.SetRow(header, row);
		Grid.SetColumnSpan(header, 2);
		rows.Children.Add(header);
	}

	private void AddRow(string header, Control editor)
	{
		rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0, GridUnitType.Auto) });
		var row = rows.RowDefinitions.Count - 1;

		var label = new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
		Grid.SetRow(label, row);
		rows.Children.Add(label);

		editor.Margin = new Thickness(0, 2, 0, 2);
		editor.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetRow(editor, row);
		Grid.SetColumn(editor, 1);
		rows.Children.Add(editor);
	}

	/// <summary>A text editor that commits on Enter or when it loses focus; empty input is discarded.</summary>
	private void AddTextBox(string header, Func<string> get, Action<string> set)
	{
		var box = new TextBox();
		void Refresh() => box.Text = get();
		void Commit()
		{
			var text = box.Text ?? string.Empty;
			if (string.IsNullOrWhiteSpace(text))
			{
				//An element with no text has no size, so it could never be picked again on the canvas.
				Refresh();
				return;
			}

			Apply(() => set(text));
			Refresh();
		}

		Refresh();
		box.LostFocus += (_, _) => Commit();
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			Commit();
			e.Handled = true;
		};

		AddRow(header, box);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A numeric editor that commits on Enter or when it loses focus; invalid input is discarded.</summary>
	private void AddNumber(string header, Func<float> get, Action<float> set)
	{
		var box = new TextBox();
		void Refresh() => box.Text = get().ToString("0.##", CultureInfo.InvariantCulture);
		void Commit()
		{
			if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			{
				Refresh();
				return;
			}

			Apply(() => set(value));
			Refresh();
		}

		Refresh();
		box.LostFocus += (_, _) => Commit();
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			Commit();
			e.Handled = true;
		};

		AddRow(header, box);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A hex color editor (#RRGGBB or #RRGGBBAA) with a live swatch of the current color.</summary>
	private void AddColor(string header, Func<SKColor> get, Action<SKColor> set)
	{
		var swatch = new Border
		{
			Width = 18,
			Height = 18,
			CornerRadius = new CornerRadius(3),
			BorderBrush = ThemeDefinition.Border,
			BorderThickness = new Thickness(1),
		};
		var box = new TextBox { MinWidth = 90 };
		var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
		editor.Children.Add(swatch);
		editor.Children.Add(box);

		void Refresh()
		{
			var color = get();
			box.Text = "#" + ToHex(color);
			swatch.Background = new ImmutableSolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
		}

		void Commit()
		{
			if (!TryParseColor(box.Text, out var color))
			{
				Refresh();
				return;
			}

			Apply(() => set(color));
			Refresh();
		}

		Refresh();
		box.LostFocus += (_, _) => Commit();
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			Commit();
			e.Handled = true;
		};

		AddRow(header, editor);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A combo box of the installed font families, with the element's family preselected.</summary>
	private void AddFontBox(string header, Func<string> get, Action<string> set)
	{
		var current = get();
		var families = FontFamilies;
		var items = families.Contains(current, StringComparer.OrdinalIgnoreCase) ? families : families.Prepend(current).ToArray();

		var combo = new ComboBox { ItemsSource = items };
		combo.SelectedItem = items.First(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
		combo.SelectionChanged += (_, _) =>
		{
			if (combo.SelectedItem is string family)
				Apply(() => set(family));
		};

		AddRow(header, combo);
		syncCallbacks.Add(() =>
		{
			if (combo.IsKeyboardFocusWithin)
				return;

			var family = get();
			if (!string.Equals(combo.SelectedItem as string, family, StringComparison.OrdinalIgnoreCase))
				combo.SelectedItem = items.FirstOrDefault(f => string.Equals(f, family, StringComparison.OrdinalIgnoreCase));
		});
	}

	/// <summary>The <paramref name="color"/> packed as RRGGBBAA hex, the same format documents are saved in.</summary>
	private static string ToHex(SKColor color) =>
		((uint)(color.Red << 24 | color.Green << 16 | color.Blue << 8 | color.Alpha))
			.ToString("X8", CultureInfo.InvariantCulture);

	/// <summary>Parses "#RRGGBB" or "#RRGGBBAA" hex colors, with or without the leading '#'; RGB gets full alpha.</summary>
	private static bool TryParseColor(string? text, out SKColor color)
	{
		color = default;

		var hex = text?.Trim().TrimStart('#');
		if (hex is null || (hex.Length != 6 && hex.Length != 8) ||
			!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
			return false;

		if (hex.Length == 6)
			packed = (packed << 8) | 0xFF;

		color = new SKColor(
			(byte)(packed >> 24 & 0xFF),
			(byte)(packed >> 16 & 0xFF),
			(byte)(packed >> 8 & 0xFF),
			(byte)(packed & 0xFF));
		return true;
	}
}
