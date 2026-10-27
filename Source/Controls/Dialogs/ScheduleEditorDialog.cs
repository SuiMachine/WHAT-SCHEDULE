using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>The schedule data the editor returns on OK: the week and the document's custom variables.</summary>
	public readonly record struct ScheduleEdit(ScheduleWeek Week, Dictionary<string, string> Variables);

	/// <summary>
	/// An editor for the schedule: one row per day with the stream's title, whether the streamer streams
	/// that day and when the stream starts, plus the custom variables. The day data (e.g. "Monday_Title",
	/// "Monday_Time") is reserved variables. OK returns the edited schedule; Cancel changes nothing.
	/// </summary>
	public sealed class ScheduleEditorDialog : Window
	{
		/// <summary>The start time the day rows' pickers are initialized with when an entry carries none.</summary>
		private static TimeOnly DefaultStreamTime => ScheduleDayEntry.DefaultStreamTime;

		private readonly List<TextBox> titleBoxes = [];
		private readonly List<CheckBox> streamingToggles = [];
		private readonly List<TimePicker> timePickers = [];

		/// <summary>The custom variable rows in the list, one per variable.</summary>
		private readonly List<VariableRow> variableRows = [];

		/// <summary>The vertical list the custom variable rows sit in.</summary>
		private readonly StackPanel variableList = new() { Spacing = 4 };

		/// <summary>Kept enabled only while every variable name is set, unique and free of the reserved day names.</summary>
		private readonly Button okButton = new() { Content = "OK", IsDefault = true };

		/// <summary>Picks how the week's stream start times are displayed, e.g. "17:30" or "5:30 PM".</summary>
		private readonly ComboBox timeFormatBox = new()
		{
			ItemsSource = new[] { "24-hour (17:30)", "12-hour (5:30 PM)" },
		};

		private ScheduleEditorDialog(ScheduleWeek week, Dictionary<string, string> variables)
		{
			Title = "Edit Week";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			CanResize = true;
			ShowInTaskbar = false;
			Background = ThemeDefinition.Background;
			Width = 560;
			MinWidth = 520;
			Height = 520;
			MinHeight = 400;

			var grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
			};

			AddHeader(grid, 0, "Day");
			AddHeader(grid, 1, "Stream title");
			AddHeader(grid, 2, "Streaming");
			AddHeader(grid, 3, "Start time");

			var row = 1;
			foreach (ScheduleDay day in Enum.GetValues<ScheduleDay>())
			{
				AddDayRow(grid, row, day.ToString(), week.Days[(int)day]);
				row++;
			}

			var cancelButton = new Button { Content = "Cancel", IsCancel = true };
			okButton.Click += (_, _) => Close(new ScheduleEdit(CollectWeek(), CollectVariables()));
			cancelButton.Click += (_, _) => Close(null);

			//Pinned to the bottom row; its top margin is the padding the variable list stops above.
			var buttons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Children = { okButton, cancelButton },
			};

			//Sits right under Sunday; the choice applies to the week like the rows above it.
			timeFormatBox.SelectedIndex = week.TimeFormat == ScheduleTimeFormat.Hour12 ? 1 : 0;
			var timeFormatSection = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = "Time format:", VerticalAlignment = VerticalAlignment.Center },
					timeFormatBox,
				},
			};

			var header = new TextBlock { Text = "Custom variables", FontWeight = FontWeight.Bold };
			var hint = new TextBlock
			{
				Text = "Names cannot be empty, duplicated or reserved; the day data (e.g. \"Monday_Title\", \"Monday_Time\") is reserved.",
				TextWrapping = TextWrapping.Wrap,
				Foreground = ThemeDefinition.RulerTick,
				Margin = new Avalonia.Thickness(0, 0, 0, 4),
			};
			var listScroll = new ScrollViewer { Content = variableList, Margin = new Avalonia.Thickness(0, 4, 0, 4) };
			var addVariableButton = new Button { Content = "Add variable", HorizontalAlignment = HorizontalAlignment.Left };
			addVariableButton.Click += (_, _) => AddVariableRow("", "");

			//The header, the hint and the add button take what they need; the list fills the height between them.
			var variablesSection = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Children = { header, hint, listScroll, addVariableButton },
			};
			Grid.SetRow(header, 0);
			Grid.SetRow(hint, 1);
			Grid.SetRow(listScroll, 2);
			Grid.SetRow(addVariableButton, 3);

			//The day rows, the time format and the buttons take what they need; the variable list fills the height between them.
			Content = new Grid
			{
				Margin = new Avalonia.Thickness(12),
				RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
				Children = { grid, timeFormatSection, variablesSection, buttons },
			};
			Grid.SetRow(grid, 0);
			Grid.SetRow(timeFormatSection, 1);
			Grid.SetRow(variablesSection, 2);
			Grid.SetRow(buttons, 3);

			foreach (var (name, value) in variables)
				AddVariableRow(name, value);
		}

		/// <summary>Opens the editor on copies of the given week and variables; returns the edited schedule only when the user confirms with OK.</summary>
		public static async Task<ScheduleEdit?> EditSchedule(Window owner, ScheduleWeek week, Dictionary<string, string> variables)
		{
			var dialog = new ScheduleEditorDialog(week.Copy(), new Dictionary<string, string>(variables, ScheduleVariables.NameComparer));
			return await dialog.ShowDialog<ScheduleEdit?>(owner);
		}

		/// <summary>Adds the bold column caption row of the grid.</summary>
		private static void AddHeader(Grid grid, int column, string caption)
		{
			var header = new TextBlock { Text = caption, FontWeight = FontWeight.Bold, Margin = new Avalonia.Thickness(0, 0, 16, 4) };
			Grid.SetColumn(header, column);
			grid.Children.Add(header);
		}

		/// <summary>Adds one day's row to the grid: the day's name, its stream's title box, streaming toggle and start time picker.</summary>
		private void AddDayRow(Grid grid, int row, string dayName, ScheduleDayEntry entry)
		{
			var name = new TextBlock { Text = dayName, Margin = new Avalonia.Thickness(0, 6, 16, 6), VerticalAlignment = VerticalAlignment.Center };
			var title = new TextBox { Text = entry.StreamTitle, PlaceholderText = "Stream title", Margin = new Avalonia.Thickness(0, 4, 16, 4) };
			var streaming = new CheckBox { IsChecked = entry.IsStreaming, Margin = new Avalonia.Thickness(0, 4, 16, 4), HorizontalAlignment = HorizontalAlignment.Center };
			var time = new TimePicker { SelectedTime = (entry.StreamTime ?? DefaultStreamTime).ToTimeSpan(), Margin = new Avalonia.Thickness(0, 4, 0, 4) };

			Grid.SetRow(name, row);
			Grid.SetRow(title, row);
			Grid.SetRow(streaming, row);
			Grid.SetRow(time, row);
			Grid.SetColumn(name, 0);
			Grid.SetColumn(title, 1);
			Grid.SetColumn(streaming, 2);
			Grid.SetColumn(time, 3);
			grid.Children.Add(name);
			grid.Children.Add(title);
			grid.Children.Add(streaming);
			grid.Children.Add(time);

			titleBoxes.Add(title);
			streamingToggles.Add(streaming);
			timePickers.Add(time);
		}

		/// <summary>Appends a custom variable row with the given name and value, or an empty one for a new variable.</summary>
		private void AddVariableRow(string name, string value)
		{
			var variableRow = new VariableRow();
			variableRow.NameBox.Text = name;
			variableRow.ValueBox.Text = value;
			variableRow.NameBox.TextChanged += (_, _) => RefreshOkButton();
			variableRow.RemoveRequested += () => RemoveVariableRow(variableRow);
			variableList.Children.Add(variableRow);
			variableRows.Add(variableRow);
			RefreshOkButton();
		}

		/// <summary>Removes a custom variable row from the list.</summary>
		private void RemoveVariableRow(VariableRow row)
		{
			variableList.Children.Remove(row);
			variableRows.Remove(row);
			RefreshOkButton();
		}

		/// <summary>Re-checks every variable name: OK stays enabled while all names are set and free, and a name
		/// that is already used (a reserved day title or another row's name) gets a red outline on its box.</summary>
		private void RefreshOkButton()
		{
			var names = new HashSet<string>(ScheduleVariables.NameComparer);
			var valid = true;
			foreach (var row in variableRows)
			{
				var name = row.NameBox.Text?.Trim();
				if (string.IsNullOrEmpty(name))
				{
					//An empty box has no name to outline; it only keeps OK disabled.
					row.NameBox.ClearValue(TextBox.BorderBrushProperty);
					valid = false;
					continue;
				}

				if (ScheduleVariables.IsReserved(name) || !names.Add(name))
				{
					row.NameBox.SetValue(TextBox.BorderBrushProperty, Brushes.Red);
					valid = false;
				}
				else
				{
					row.NameBox.ClearValue(TextBox.BorderBrushProperty);
				}
			}

			okButton.IsEnabled = valid;
		}

		/// <summary>Reads the edited week back from the controls, one entry per row.</summary>
		private ScheduleWeek CollectWeek()
		{
			var week = new ScheduleWeek { TimeFormat = timeFormatBox.SelectedIndex == 1 ? ScheduleTimeFormat.Hour12 : ScheduleTimeFormat.Hour24 };
			for (var day = 0; day < week.Days.Length; day++)
			{
				week.Days[day] = new ScheduleDayEntry
				{
					StreamTitle = titleBoxes[day].Text ?? "",
					IsStreaming = streamingToggles[day].IsChecked == true,
					StreamTime = timePickers[day].SelectedTime is { } span ? TimeOnly.FromTimeSpan(span) : null,
				};
			}

			return week;
		}

		/// <summary>Reads the custom variables back from the list's name and value boxes.</summary>
		private Dictionary<string, string> CollectVariables()
		{
			var variables = new Dictionary<string, string>(ScheduleVariables.NameComparer);
			foreach (var row in variableRows)
				variables[row.NameBox.Text!.Trim()] = row.ValueBox.Text ?? "";
			return variables;
		}

		/// <summary>One custom variable's row in the list: its name and value box and the button removing the row.</summary>
		private sealed class VariableRow : Grid
		{
			/// <summary>The variable's name box; an empty, reserved or duplicated name keeps OK disabled.</summary>
			public TextBox NameBox { get; } = new() { PlaceholderText = "Name" };

			/// <summary>The variable's value box.</summary>
			public TextBox ValueBox { get; } = new() { PlaceholderText = "Value" };

			/// <summary>Raised when the row's remove button is clicked.</summary>
			public event Action? RemoveRequested;

			public VariableRow()
			{
				ColumnDefinitions = new ColumnDefinitions("160,*,Auto");
				var remove = new Button { Content = "Remove", Margin = new Avalonia.Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				remove.Click += (_, _) => RemoveRequested?.Invoke();

				Grid.SetColumn(NameBox, 0);
				Grid.SetColumn(ValueBox, 1);
				Grid.SetColumn(remove, 2);
				Children.Add(NameBox);
				Children.Add(ValueBox);
				Children.Add(remove);
			}
		}
	}
}
