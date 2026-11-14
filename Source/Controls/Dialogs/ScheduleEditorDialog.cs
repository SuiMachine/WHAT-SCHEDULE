using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.SpellChecker;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>The schedule data the editor returns on OK: the week and the document's custom variables.</summary>
	public readonly record struct ScheduleEdit(ScheduleWeek Week, Dictionary<string, string> Variables);

	/// <summary>
	/// An editor for the schedule: one row per day with the stream's title, whether the streamer streams
	/// that day and when the stream starts, plus the week's time zone, its converted times and the custom variables.
	/// The day data (e.g. "Monday_Time", "Monday_Time_ET") is reserved variables. OK returns the edited schedule;
	/// Cancel changes nothing.
	/// </summary>
	public sealed class ScheduleEditorDialog : Window
	{
		/// <summary>The start time the day rows' pickers are initialized with when an entry carries none.</summary>
		private static TimeOnly DefaultStreamTime => ScheduleDayEntry.DefaultStreamTime;

		/// <summary>The week the editor was opened on, whose start, date culture and zone the edited week keeps.</summary>
		private readonly ScheduleWeek week;

		/// <summary>The zone the edited week's times belong to, as a portable ID; null means the system's local zone.</summary>
		private string? homeZoneId;

		private readonly List<TextBox> titleBoxes = [];
		private readonly List<CheckBox> streamingToggles = [];
		private readonly List<TimePicker> timePickers = [];

		/// <summary>The custom variable rows in the list, one per variable.</summary>
		private readonly List<VariableRow> variableRows = [];

		/// <summary>The vertical list the custom variable rows sit in.</summary>
		private readonly StackPanel variableList = new() { Spacing = 4 };

		/// <summary>The conversion rows in the list, one per converted time zone.</summary>
		private readonly List<ConversionRow> conversionRows = [];

		/// <summary>The vertical list the conversion rows sit in.</summary>
		private readonly StackPanel conversionList = new() { Spacing = 4 };

		/// <summary>Kept enabled only while every variable name is set, unique and free of the reserved names, and every conversion suffix is set, well-formed and unique.</summary>
		private readonly Button okButton = new() { Content = "OK", IsDefault = true };

		/// <summary>Picks how the week's stream start times are displayed, e.g. "17:30" or "5:30 PM".</summary>
		private readonly ComboBox timeFormatBox = new()
		{
			ItemsSource = new[] { "24-hour (17:30)", "12-hour (5:30 PM)" },
		};

		private ScheduleEditorDialog(ScheduleWeek week, Dictionary<string, string> variables)
		{
			this.week = week;
			homeZoneId = week.TimeZoneId;
			Title = "Edit Week";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			CanResize = true;
			ShowInTaskbar = false;
			Background = ThemeDefinition.Background;
			Width = 560;
			MinWidth = 520;
			MinHeight = 400;
			//The window opens as tall as its content and stops fitting itself once shown, so it stays resizable.
			SizeToContent = SizeToContent.Height;
			Opened += (_, _) => SizeToContent = SizeToContent.Manual;

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

			//Pinned below the scrolling sections, so OK and Cancel stay visible however small the window gets.
			var buttons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Children = { okButton, cancelButton },
			};

			//The settings sit right under Sunday; each choice applies to the week like the rows above it.
			timeFormatBox.SelectedIndex = week.TimeFormat == ScheduleTimeFormat.Hour12 ? 1 : 0;

			//The zone the week's start times belong to; the conversions convert out of it.
			var homeZoneButton = new Button { Content = ZoneButtonText(homeZoneId) };
			homeZoneButton.Click += async (_, _) =>
			{
				if (await TimeZonePickerDialog.PickZone(this) is { } selection)
				{
					homeZoneId = selection.TimeZoneId;
					homeZoneButton.Content = ZoneButtonText(homeZoneId);
				}
			};

			var settingsSection = new StackPanel
			{
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Spacing = 6,
				Children =
				{
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 6,
						Children =
						{
							new TextBlock { Text = "Time format:", VerticalAlignment = VerticalAlignment.Center },
							timeFormatBox,
						},
					},
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 6,
						Children =
						{
							new TextBlock { Text = "Time zone:", VerticalAlignment = VerticalAlignment.Center },
							homeZoneButton,
						},
					},
				},
			};

			//The header, the hint and the add button take what they need; the list scrolls within the room left to it.
			var conversionsHeader = new TextBlock { Text = "Converted times", FontWeight = FontWeight.Bold };
			var conversionsHint = new TextBlock
			{
				Text = "Each conversion shows every day's start time in its zone, e.g. suffix \"ET\" for New York yields \"Monday_Time_ET\" and \"Monday_Weekday_ET\".",
				TextWrapping = TextWrapping.Wrap,
				Foreground = ThemeDefinition.RulerTick,
				Margin = new Avalonia.Thickness(0, 0, 0, 4),
			};
			var conversionsScroll = new ScrollViewer { Content = conversionList, MaxHeight = 140, Margin = new Avalonia.Thickness(0, 4, 0, 4) };
			var addConversionButton = new Button { Content = "Add conversion", HorizontalAlignment = HorizontalAlignment.Left };
			addConversionButton.Click += (_, _) => AddConversionRow("", null);

			var conversionsSection = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Children = { conversionsHeader, conversionsHint, conversionsScroll, addConversionButton },
			};
			Grid.SetRow(conversionsHeader, 0);
			Grid.SetRow(conversionsHint, 1);
			Grid.SetRow(conversionsScroll, 2);
			Grid.SetRow(addConversionButton, 3);

			var header = new TextBlock { Text = "Custom variables", FontWeight = FontWeight.Bold };
			var hint = new TextBlock
			{
				Text = "Names cannot be empty, duplicated or reserved; the day data (e.g. \"Monday_Title\", \"Monday_Time\") and its converted times (e.g. \"Monday_Time_ET\") are reserved.",
				TextWrapping = TextWrapping.Wrap,
				Foreground = ThemeDefinition.RulerTick,
				Margin = new Avalonia.Thickness(0, 0, 0, 4),
			};
			//The list scrolls within its cap instead of growing the window without bound.
			var listScroll = new ScrollViewer { Content = variableList, MaxHeight = 200, Margin = new Avalonia.Thickness(0, 4, 0, 4) };
			var addVariableButton = new Button { Content = "Add variable", HorizontalAlignment = HorizontalAlignment.Left };
			addVariableButton.Click += (_, _) => AddVariableRow("", "");

			//The header, the hint, the capped list and the add button each take what they need.
			var variablesSection = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
				Margin = new Avalonia.Thickness(0, 12, 0, 0),
				Children = { header, hint, listScroll, addVariableButton },
			};
			Grid.SetRow(header, 0);
			Grid.SetRow(hint, 1);
			Grid.SetRow(listScroll, 2);
			Grid.SetRow(addVariableButton, 3);

			//The sections scroll when the window is shorter than they need; the buttons stay pinned to the bottom row.
			var bodyScroll = new ScrollViewer { Content = new StackPanel { Children = { grid, settingsSection, conversionsSection, variablesSection } } };
			Content = new Grid
			{
				Margin = new Avalonia.Thickness(12),
				RowDefinitions = new RowDefinitions("*,Auto"),
				Children = { bodyScroll, buttons },
			};
			Grid.SetRow(bodyScroll, 0);
			Grid.SetRow(buttons, 1);

			foreach (var conversion in week.Conversions)
				AddConversionRow(conversion.Suffix, conversion.TimeZoneId);
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
			SpellCheckerUniversalInstance.Instance.Initialize(title);

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

		/// <summary>Appends a conversion row with the given suffix and zone, or an empty one for a new conversion.</summary>
		private void AddConversionRow(string suffix, string? timeZoneId)
		{
			var conversionRow = new ConversionRow { TimeZoneId = timeZoneId };
			conversionRow.SuffixBox.Text = suffix;
			conversionRow.ZoneButton.Content = ZoneButtonText(timeZoneId);
			conversionRow.SuffixBox.TextChanged += (_, _) => RefreshOkButton();
			conversionRow.ZoneButton.Click += async (_, _) =>
			{
				if (await TimeZonePickerDialog.PickZone(this) is { } selection)
				{
					conversionRow.TimeZoneId = selection.TimeZoneId;
					conversionRow.ZoneButton.Content = ZoneButtonText(conversionRow.TimeZoneId);
				}
			};
			conversionRow.RemoveRequested += () => RemoveConversionRow(conversionRow);
			conversionList.Children.Add(conversionRow);
			conversionRows.Add(conversionRow);
			RefreshOkButton();
		}

		/// <summary>Removes a conversion row from the list.</summary>
		private void RemoveConversionRow(ConversionRow row)
		{
			conversionList.Children.Remove(row);
			conversionRows.Remove(row);
			RefreshOkButton();
		}

		/// <summary>The zone a portable ID names, as the pickers show it, e.g. "(UTC+01:00) Warszawa"; a null ID is the system's local zone.</summary>
		private static string ZoneButtonText(string? id) => id is null ? "System local" : ScheduleZones.Resolve(id).DisplayName;

		/// <summary>Whether a conversion suffix can build variable names: set, and letters, digits and underscores only.</summary>
		private static bool IsValidSuffix(string suffix) =>
			suffix.Length > 0 && suffix.All(character => char.IsLetterOrDigit(character) || character == '_');

		/// <summary>Re-checks every conversion suffix and variable name: OK stays enabled while every suffix is set,
		/// well-formed and unique, and every variable name is set and free; anything that is already used (a reserved day
		/// name, a name a conversion builds, or another row's name) gets a red outline on its box.</summary>
		private void RefreshOkButton()
		{
			//The well-formed suffixes the rows currently build variable names from; the malformed ones only keep OK disabled.
			var conversions = new List<ScheduleZoneConversion>();
			var suffixes = new HashSet<string>(ScheduleVariables.NameComparer);
			var valid = true;
			foreach (var row in conversionRows)
			{
				var suffix = row.SuffixBox.Text?.Trim() ?? "";
				if (!IsValidSuffix(suffix) || !suffixes.Add(suffix))
				{
					row.SuffixBox.SetValue(TextBox.BorderBrushProperty, Brushes.Red);
					valid = false;
				}
				else
				{
					row.SuffixBox.ClearValue(TextBox.BorderBrushProperty);
					conversions.Add(new ScheduleZoneConversion { Suffix = suffix, TimeZoneId = row.TimeZoneId });
				}
			}

			var names = new HashSet<string>(ScheduleVariables.NameComparer);
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

				if (ScheduleVariables.IsReserved(name, conversions) || !names.Add(name))
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

		/// <summary>Reads the edited week back from the controls, one entry per row. The week's start, date culture and
		/// time zone carry over from the week the editor was opened on; the day rows and conversions come from the controls.</summary>
		private ScheduleWeek CollectWeek()
		{
			var edited = new ScheduleWeek
			{
				TimeFormat = timeFormatBox.SelectedIndex == 1 ? ScheduleTimeFormat.Hour12 : ScheduleTimeFormat.Hour24,
				//The week picker owns the week's start and date culture; editing the day rows must not reset them.
				WeekStart = week.WeekStart,
				DateCulture = week.DateCulture,
				TimeZoneId = homeZoneId,
				Conversions = [.. conversionRows.Select(row => new ScheduleZoneConversion { Suffix = row.SuffixBox.Text!.Trim(), TimeZoneId = row.TimeZoneId })],
			};
			for (var day = 0; day < edited.Days.Length; day++)
			{
				edited.Days[day] = new ScheduleDayEntry
				{
					StreamTitle = titleBoxes[day].Text ?? "",
					IsStreaming = streamingToggles[day].IsChecked == true,
					StreamTime = timePickers[day].SelectedTime is { } span ? TimeOnly.FromTimeSpan(span) : null,
				};
			}

			return edited;
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

		/// <summary>One conversion's row in the list: its suffix box, the zone it converts into and the button removing the row.</summary>
		private sealed class ConversionRow : Grid
		{
			/// <summary>The conversion's suffix box; an empty, malformed or duplicated suffix keeps OK disabled.</summary>
			public TextBox SuffixBox { get; } = new() { PlaceholderText = "Suffix" };

			/// <summary>The button showing the zone the conversion converts into; clicking it opens the zone picker.</summary>
			public Button ZoneButton { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

			/// <summary>The zone the conversion converts into, as a portable ID; null converts into the system's local zone.</summary>
			public string? TimeZoneId { get; set; }

			/// <summary>Raised when the row's remove button is clicked.</summary>
			public event Action? RemoveRequested;

			public ConversionRow()
			{
				ColumnDefinitions = new ColumnDefinitions("90,*,Auto");
				var remove = new Button { Content = "Remove", Margin = new Avalonia.Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
				remove.Click += (_, _) => RemoveRequested?.Invoke();

				Grid.SetColumn(SuffixBox, 0);
				Grid.SetColumn(ZoneButton, 1);
				Grid.SetColumn(remove, 2);
				Children.Add(SuffixBox);
				Children.Add(ZoneButton);
				Children.Add(remove);
			}
		}
	}
}
