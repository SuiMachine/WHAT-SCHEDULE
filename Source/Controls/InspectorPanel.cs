using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SkiaSharp;
using System.Globalization;
using WhatSchedule.Controls.Dialogs;
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

	/// <summary>Titles of the sections the user collapsed, kept across rebuilds so the choice sticks when the selection changes.</summary>
	private readonly HashSet<string> collapsedSections = new(StringComparer.Ordinal);

	/// <summary>The editors of the section being built, hidden while its header is collapsed.</summary>
	private List<Control> sectionControls = new();

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
			Text = element.Kind,
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
		AddNumber("Rotation", () => element.Rotation, value => element.Rotation = value);

		AddSection("Layer");
		AddLayerRow(element);

		element.BuildInspectorRows(this);
	}

	/// <summary>The <see cref="IInspectorBuilder"/> operations the selected element builds its rows with.</summary>
	void IInspectorBuilder.AddSection(string title) => AddSection(title);
	void IInspectorBuilder.AddRow(string header, Control editor) => AddRow(header, editor);
	void IInspectorBuilder.AddTextBox(string header, Func<string> getter, Action<string> setter) => AddTextBox(header, getter, setter);
	void IInspectorBuilder.AddTextArea(string header, Func<string> getter, Action<string> setter) => AddTextArea(header, getter, setter);
	void IInspectorBuilder.AddComboBox(string header, string[] items, Func<string> getter, Action<string> setter) => AddComboBox(header, items, getter, setter);
	void IInspectorBuilder.AddNumber(string header, Func<float> getter, Action<float> setter, float? minimum, float? maximum) => AddNumber(header, getter, setter, minimum, maximum);
	void IInspectorBuilder.AddToggle(string header, Func<bool> getter, Action<bool> setter) => AddToggle(header, getter, setter);
	void IInspectorBuilder.AddSlider(string header, float minimum, float maximum, Func<float> getter, Action<float> setter) => AddSlider(header, minimum, maximum, getter, setter);
	void IInspectorBuilder.AddColor(string header, Func<SKColor> getter, Action<SKColor> setter) => AddColor(header, getter, setter);
	void IInspectorBuilder.AddVariablePicker(Action<string> insert) => AddVariablePicker(insert);
	void IInspectorBuilder.AddFontBox(string header, Func<string> getter, Action<string> setter) => AddFontBox(header, getter, setter);
	void IInspectorBuilder.AddSync(Action sync) => syncCallbacks.Add(sync);
	void IInspectorBuilder.Apply(string description, Action mutation) => Apply(description, mutation);

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
			//Layering moves an element within its own container: its group, or the document root.
			var elements = canvas?.Document?.SiblingsOf(element);
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

	/// <summary>Applies <paramref name="mutation"/> to the selected element and records it in the undo
	/// history as <paramref name="description"/>, which the undo and redo commands then show.</summary>
	private void Apply(string description, Action mutation)
	{
		var document = canvas?.Document;
		if (element is null || document is null)
			return;

		var before = element.CaptureState();
		mutation();
		document.RecordStateChange(element, before, description);
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
		var controls = new List<Control>();
		sectionControls = controls;

		rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0, GridUnitType.Auto) });
		var row = rows.RowDefinitions.Count - 1;

		var arrow = new Avalonia.Controls.Shapes.Path
		{
			Width = 8,
			Height = 8,
			Stretch = Stretch.Uniform,
			StrokeThickness = 1.5,
			Stroke = ThemeDefinition.RulerLabel,
			Margin = new Thickness(1, 0, 5, 0),
		};
		//A themed button would repaint itself through its checked and pressed states, so the
		//header is a plain panel that paints its own hover highlight: same look in both states.
		var header = new Border
		{
			Background = Brushes.Transparent,
			Margin = new Thickness(0, 10, 0, 2),
			Cursor = new Cursor(StandardCursorType.Hand),
			Focusable = true,
			Child = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Children =
				{
					arrow,
					new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
				},
			},
		};

		var expanded = !collapsedSections.Contains(title);
		void SetExpanded(bool value)
		{
			expanded = value;
			arrow.Data = Geometry.Parse(value ? "M0,2 L4.5,6 L9,2" : "M2,0 L6,4.5 L2,9");
			foreach (var control in controls)
				control.IsVisible = value;
		}

		void Toggle()
		{
			SetExpanded(!expanded);
			if (expanded)
				collapsedSections.Remove(title);
			else
				collapsedSections.Add(title);
		}

		SetExpanded(expanded);
		header.PointerPressed += (_, e) =>
		{
			if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
				return;

			Toggle();
			e.Handled = true;
		};
		header.KeyDown += (_, e) =>
		{
			if (e.Key is not (Key.Space or Key.Enter))
				return;

			Toggle();
			e.Handled = true;
		};
		header.PointerEntered += (_, _) => header.Background = ThemeDefinition.RulerBackground;
		header.PointerExited += (_, _) => header.Background = Brushes.Transparent;

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

		sectionControls.Add(label);
		sectionControls.Add(editor);
	}

	/// <summary>A text editor that previews every keystroke on the canvas and records the whole edit
	/// run as one undo entry when it ends on Enter or focus loss; empty input reverts the element.</summary>
	private void AddTextBox(string header, Func<string> get, Action<string> set)
	{
		var box = new TextBox();

		//Value changes that come from the sync refresh must not start an edit or commit anything.
		var syncing = false;
		var editStart = default(CanvasElement.ElementState);

		void Refresh()
		{
			syncing = true;
			box.Text = get();
			syncing = false;
		}

		void Commit()
		{
			if (editStart is not { } before)
				return;

			//An element with no text could never be picked again on the canvas, so clearing the
			//field and leaving it reverts the element instead of recording a change.
			if (string.IsNullOrWhiteSpace(box.Text))
			{
				editStart = null;
				if (element is not null)
				{
					element.RestoreState(before);
					canvas?.Document?.RaiseChanged();
				}

				Refresh();
				return;
			}

			if (element is not null)
				canvas?.Document?.RecordStateChange(element, before, header);
			editStart = null;
		}

		box.TextChanged += (_, _) =>
		{
			if (syncing)
				return;

			editStart ??= element?.CaptureState();
			set(box.Text ?? string.Empty);
			canvas?.Document?.RaiseChanged();
		};
		box.LostFocus += (_, _) => Commit();
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			Commit();
			e.Handled = true;
		};

		Refresh();
		AddRow(header, box);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A multi-line text editor that previews every keystroke on the canvas and records the whole
	/// edit run as one undo entry when the field loses focus. Enter adds a line break, and clearing the
	/// field and leaving it reverts the element, because an element with no text could never be picked again on the canvas.</summary>
	private void AddTextArea(string header, Func<string> get, Action<string> set)
	{
		var box = new TextBox
		{
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			MinHeight = 64,
			MaxHeight = 140,
			VerticalContentAlignment = VerticalAlignment.Top,
		};

		//Value changes that come from the sync refresh must not start an edit or commit anything.
		var syncing = false;
		var editStart = default(CanvasElement.ElementState);

		void Refresh()
		{
			syncing = true;
			box.Text = get();
			syncing = false;
		}

		box.TextChanged += (_, _) =>
		{
			if (syncing)
				return;

			//The TextBox inserts Windows line breaks; the label's text is stored with plain ones.
			editStart ??= element?.CaptureState();
			set((box.Text ?? string.Empty).Replace("\r\n", "\n"));
			canvas?.Document?.RaiseChanged();
		};

		Refresh();
		box.LostFocus += (_, _) =>
		{
			if (editStart is not { } before)
				return;

			if (string.IsNullOrWhiteSpace(box.Text))
			{
				editStart = null;
				if (element is not null)
				{
					element.RestoreState(before);
					canvas?.Document?.RaiseChanged();
				}

				Refresh();
				return;
			}

			if (element is not null)
				canvas?.Document?.RecordStateChange(element, before, header);
			editStart = null;
		};

		AddRow(header, box);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A combo box of the given items that commits as soon as the selection changes; only user interaction raises the changed event, so the sync refresh never records undo states.</summary>
	private void AddComboBox(string header, string[] items, Func<string> get, Action<string> set)
	{
		var combo = new ComboBox { ItemsSource = items, SelectedItem = get() };
		combo.SelectionChanged += (_, _) =>
		{
			if (combo.SelectedItem is string value && !string.Equals(value, get(), StringComparison.Ordinal))
				Apply(header, () => set(value));
		};

		AddRow(header, combo);
		syncCallbacks.Add(() =>
		{
			if (combo.IsKeyboardFocusWithin)
				return;

			var current = get();
			if (!string.Equals(combo.SelectedItem as string, current, StringComparison.Ordinal))
				combo.SelectedItem = items.FirstOrDefault(item => string.Equals(item, current, StringComparison.Ordinal)) ?? combo.SelectedItem;
		});
	}

	/// <summary>A numeric editor with spin buttons. The value is previewed live while it changes and a whole
	/// edit run (typing, a pointer drag on a spin button, or a run of held keyboard steps) commits as a
	/// single undo entry when the run ends on Enter or focus loss; input the control cannot parse is discarded.</summary>
	private void AddNumber(string header, Func<float> get, Action<float> set, float? minimum = null, float? maximum = null)
	{
		//The bounds are decimal's own extremes when absent; a float bound always fits inside that range.
		var box = new NumericUpDown
		{
			FormatString = "0.##",
			NumberFormat = CultureInfo.InvariantCulture.NumberFormat,
			Minimum = (decimal?)minimum ?? decimal.MinValue,
			Maximum = (decimal?)maximum ?? decimal.MaxValue
		};

		//Value changes that come from the sync refresh must not start an edit or commit anything.
		var syncing = false;
		var editStart = default(CanvasElement.ElementState);

		void Refresh()
		{
			syncing = true;
			box.Value = (decimal)get();
			syncing = false;
		}

		void Commit()
		{
			if (editStart is { } before && element is not null)
				canvas?.Document?.RecordStateChange(element, before, header);
			editStart = null;
		}

		box.ValueChanged += (_, _) =>
		{
			if (syncing || box.Value is not { } value)
				return;

			editStart ??= element?.CaptureState();
			set((float)value);
			canvas?.Document?.RaiseChanged();
		};
		//A setter may clamp what was typed into something legal; re-reading on commit keeps the box from showing a value that never landed.
		box.LostFocus += (_, _) => { Commit(); Refresh(); };
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			Commit();
			Refresh();
			e.Handled = true;
		};

		Refresh();
		AddRow(header, box);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A switch that commits as soon as it is toggled. Only user clicks raise the switch's
	/// click event, so the sync refreshes setting its state never record undo states.</summary>
	private void AddToggle(string header, Func<bool> get, Action<bool> set)
	{
		var toggle = new ToggleSwitch();
		void Refresh() => toggle.IsChecked = get();
		Refresh();
		toggle.Click += (_, _) => Apply(header, () => set(toggle.IsChecked == true));

		AddRow(header, toggle);
		syncCallbacks.Add(() => { if (!toggle.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A slider with an editable number beside it. The value is previewed live while it changes,
	/// a whole pointer drag or a run of held keyboard steps commits as a single undo entry, and typing a
	/// number commits like any other numeric editor: on Enter or focus loss, invalid input discarded.</summary>
	private void AddSlider(string header, float minimum, float maximum, Func<float> get, Action<float> set)
	{
		var slider = new Slider
		{
			Minimum = minimum,
			Maximum = maximum,
			SmallChange = 1,
			LargeChange = 10,
			MinWidth = 60,
			//The Fluent slider template is 50 tall, which would stretch the row and the
			//text box with it; 32 matches the text box height so the row lines up with its neighbours.
			//The template also draws its track in the middle of its natural 50px, so the negative
			//top padding pulls the track up to the middle of the shrunk 32px control.
			Height = 32,
			Padding = new Thickness(0, -9, 0, 0),
		};
		var box = new TextBox { MinWidth = 48 };

		var editor = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		Grid.SetColumn(slider, 0);
		Grid.SetColumn(box, 1);
		editor.Children.Add(slider);
		editor.Children.Add(box);

		//Value changes that come from the sync refresh must not start a drag or commit anything.
		var syncing = false;
		var dragStart = default(CanvasElement.ElementState);
		void CommitDrag()
		{
			if (dragStart is { } before && element is not null)
				canvas?.Document?.RecordStateChange(element, before, header);
			dragStart = null;
		}

		void Refresh()
		{
			syncing = true;
			var current = get();
			slider.Value = current;
			box.Text = current.ToString("0.##", CultureInfo.InvariantCulture);
			syncing = false;
		}

		void CommitTyped()
		{
			if (float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
				Apply(header, () => set((float)Math.Clamp(typed, minimum, maximum)));
			Refresh();
		}

		slider.ValueChanged += (_, _) =>
		{
			if (syncing)
				return;

			var current = (float)slider.Value;
			if (!box.IsKeyboardFocusWithin)
				box.Text = current.ToString("0.##", CultureInfo.InvariantCulture);
			dragStart ??= element?.CaptureState();
			set(current);
			canvas?.Document?.RaiseChanged();
		};
		//A pointer drag ends when the thumb loses the pointer capture; a keyboard
		//run ends when the held key is released. Either way, one undo entry per run.
		slider.PointerCaptureLost += (_, _) => CommitDrag();
		slider.KeyUp += (_, _) => CommitDrag();

		box.LostFocus += (_, _) => CommitTyped();
		box.KeyDown += (_, e) =>
		{
			if (e.Key != Key.Enter)
				return;

			CommitTyped();
			e.Handled = true;
		};

		Refresh();
		AddRow(header, editor);
		syncCallbacks.Add(() => { if (!slider.IsKeyboardFocusWithin && !box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A hex color editor (#RRGGBB or #RRGGBBAA) with a live swatch that opens the color picker dialog when pressed.</summary>
	private void AddColor(string header, Func<SKColor> get, Action<SKColor> set)
	{
		var swatchFill = new Border
		{
			CornerRadius = new CornerRadius(3),
			BorderBrush = ThemeDefinition.Border,
			BorderThickness = new Thickness(1),
		};
		var swatch = new Button
		{
			Width = 18,
			Height = 18,
			Padding = new Thickness(0),
			Content = swatchFill,
			Cursor = new Cursor(StandardCursorType.Hand),
		};

		var box = new TextBox { Margin = new Thickness(6, 2, 0, 2) };
		var editor = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
		Grid.SetColumn(swatch, 0);
		Grid.SetColumn(box, 1);
		editor.Children.Add(swatch);
		editor.Children.Add(box);

		void Refresh()
		{
			var color = get();
			box.Text = "#" + ToHex(color);
			swatchFill.Background = new ImmutableSolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
		}

		void Commit()
		{
			if (!TryParseColor(box.Text, out var color))
			{
				Refresh();
				return;
			}

			Apply(header, () => set(color));
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

		swatch.Click += async (_, _) =>
		{
			var owner = TopLevel.GetTopLevel(this) as Window;
			if (owner is null)
				return;

			var current = get();
			var picked = await ColorPickerDialog.ShowDialog(owner, current);
			if (picked is not { } value)
				return;

			Apply(header, () => set(picked.Value));
			Refresh();
		};
		AddRow(header, editor);
		syncCallbacks.Add(() => { if (!box.IsKeyboardFocusWithin) Refresh(); });
	}

	/// <summary>A button that opens the schedule's variables; the name of the one the user picks is handed to
	/// <paramref name="insert"/>, recorded in the undo history as a single step.</summary>
	private void AddVariablePicker(Action<string> insert)
	{
		var button = new Button { Content = "Use variable", HorizontalAlignment = HorizontalAlignment.Left };
		button.Click += async (_, _) =>
		{
			var owner = TopLevel.GetTopLevel(this) as Window;
			var document = canvas?.Document;
			if (owner is null || document is null)
				return;

			if (await VariableListDialog.PickVariable(owner, ScheduleVariables.Collect(document)) is { } name)
				Apply("Use variable", () => insert(name));
		};

		AddRow(string.Empty, button);
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
				Apply(header, () => set(family));
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
