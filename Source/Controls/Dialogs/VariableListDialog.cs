using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>A picker for the schedule's defined variables: one is picked by its category, then by the time
	/// zone it belongs to, then by its day and format, so the schedule's whole variable space never shows as one
	/// list. The steps slide in the direction the picker travels. Picking a variable returns its name for the caller to insert.</summary>
	public sealed class VariableListDialog : Window
	{
		/// <summary>The kinds the picker groups the schedule's variables into.</summary>
		private enum VariableCategory
		{
			Date,
			Time,
			Title,
			Custom,
		}

		/// <summary>The value every defined variable currently produces, by the name it is referenced by.</summary>
		private readonly Dictionary<string, string> values;

		/// <summary>The week's named time zone conversions, each offering its own set of variables.</summary>
		private readonly List<ScheduleZoneConversion> conversions;

		/// <summary>The custom variables the document defines, in their defined order.</summary>
		private readonly (string Name, string Value)[] custom;

		/// <summary>The line above the current step, naming what the step asks for.</summary>
		private readonly TextBlock caption = new() { Foreground = ThemeDefinition.RulerTick };

		/// <summary>Shows the current step's controls, sliding the old step out and the new one in;
		/// the step is swapped by giving the host new content.</summary>
		private readonly TransitioningContentControl stepHost = new()
		{
			PageTransition = new PageSlide(TimeSpan.FromMilliseconds(200), PageSlide.SlideAxis.Horizontal),
		};

		/// <summary>Returns to the previous step; hidden in the first one.</summary>
		private readonly Button backButton = new() { Content = "Back", IsVisible = false };

		/// <summary>The category picked in the first step.</summary>
		private VariableCategory category;

		/// <summary>The step the picker is on, so going back returns to the one before it.</summary>
		private int depth;

		/// <summary>The time zone picked in the second step: null picks the schedule's base variables,
		/// a suffix picks the conversion's converted ones.</summary>
		private string? suffix;

		private VariableListDialog(Document document)
		{
			Title = "Variables";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			MinWidth = 300;
			Background = ThemeDefinition.Background;

			values = ScheduleVariables.Collect(document).ToDictionary(variable => variable.Name, variable => variable.Value ?? string.Empty, ScheduleVariables.NameComparer);
			conversions = document.Week.Conversions;
			custom = [.. document.Variables.Select(pair => (pair.Key, pair.Value))];

			backButton.Click += (_, _) => GoBack();
			Content = new StackPanel
			{
				Margin = new Thickness(12),
				Spacing = 8,
				Children = { caption, stepHost, backButton },
			};

			ShowCategory();
		}

		/// <summary>Shows the variables; returns the picked variable's name, or null when nothing was picked.</summary>
		public static async Task<string?> PickVariable(Window owner, Document document)
		{
			var dialog = new VariableListDialog(document);
			return await dialog.ShowDialog<string?>(owner);
		}

		/// <summary>The first step: the category, with the custom one offered only when the document defines any.</summary>
		private void ShowCategory(bool backwards = false)
		{
			depth = 0;
			backButton.IsVisible = false;
			caption.Text = "Pick a category.";
			var panel = NewStep(backwards);
			Choice(panel, "Date", "The days' dates", () => ShowTimeZone(VariableCategory.Date));
			Choice(panel, "Time", "The days' stream start times", () => ShowTimeZone(VariableCategory.Time));
			Choice(panel, "Title", "The days' stream titles", () =>
			{
				category = VariableCategory.Title;
				suffix = null;
				ShowDays();
			});
			Choice(panel, "Custom", "The variables you defined", () =>
			{
				category = VariableCategory.Custom;
				ShowCustom();
			}, custom.Length > 0);
		}

		/// <summary>The second step: the time zone the variables belong to, skipped when the week defines no conversion.</summary>
		private void ShowTimeZone(VariableCategory picked, bool backwards = false)
		{
			category = picked;

			//Without conversions there is nothing to choose between, so the base variables are shown right away.
			if (conversions.Count == 0)
			{
				suffix = null;
				ShowDays();
				return;
			}

			backButton.IsVisible = true;
			depth = 1;
			caption.Text = "Pick the time zone.";
			var panel = NewStep(backwards);
			Choice(panel, "Base", "The schedule's own time zone", () =>
			{
				suffix = null;
				ShowDays();
			});
			foreach (var conversion in conversions)
				Choice(panel, conversion.Suffix, "Converted into " + ScheduleZones.PortableId(ScheduleZones.Resolve(conversion.TimeZoneId)), () =>
				{
					suffix = conversion.Suffix;
					ShowDays();
				});
		}

		/// <summary>The last step for the date, time and title categories: one row per day, one column per format,
		/// each cell showing the value the variable currently produces and naming the variable in its tooltip.</summary>
		private void ShowDays()
		{
			depth = 2;
			backButton.IsVisible = true;
			caption.Text = "Pick the day" + (category == VariableCategory.Date ? " and the format." : ".");
			var panel = NewStep(false);

			(string Label, Func<ScheduleDay, string> Name)[] formats = category switch
			{
				VariableCategory.Date when suffix is null =>
				[
					("Date", ScheduleVariables.DateName),
					("Long Date", ScheduleVariables.DateLongName),
					("Day of Month", ScheduleVariables.DayName),
				],
				VariableCategory.Date =>
				[
					("Date", day => ScheduleVariables.ConvertedDateName(day, suffix!)),
					("Long Date", day => ScheduleVariables.ConvertedDateLongName(day, suffix!)),
					("Day of Month", day => ScheduleVariables.ConvertedDayName(day, suffix!)),
					("Weekday", day => ScheduleVariables.ConvertedWeekdayName(day, suffix!)),
				],
				VariableCategory.Time when suffix is null => [("Time", ScheduleVariables.TimeName)],
				VariableCategory.Time => [("Time", day => ScheduleVariables.ConvertedTimeName(day, suffix!))],
				_ => [("Title", ScheduleVariables.TitleName)],
			};

			var days = Enum.GetValues<ScheduleDay>();
			var grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("Auto", formats.Length + 1))),
				RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", days.Length + 1))),
			};

			//The header row names the formats; below it one row per day follows.
			for (var column = 0; column < formats.Length; column++)
			{
				var label = new TextBlock { Text = formats[column].Label, Foreground = ThemeDefinition.RulerTick, Margin = new Thickness(0, 0, 8, 4) };
				Grid.SetRow(label, 0);
				Grid.SetColumn(label, column + 1);
				grid.Children.Add(label);
			}

			for (var row = 0; row < days.Length; row++)
			{
				var day = days[row];
				var dayLabel = new TextBlock { Text = day.ToString(), Foreground = ThemeDefinition.RulerLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) };
				Grid.SetRow(dayLabel, row + 1);
				Grid.SetColumn(dayLabel, 0);
				grid.Children.Add(dayLabel);

				for (var column = 0; column < formats.Length; column++)
				{
					var value = ValueButton(formats[column].Name(day));
					Grid.SetRow(value, row + 1);
					Grid.SetColumn(value, column + 1);
					grid.Children.Add(value);
				}
			}

			panel.Children.Add(grid);
		}

		/// <summary>The last step for the custom category: the variables the document defines, with the value each currently produces.</summary>
		private void ShowCustom()
		{
			depth = 2;
			backButton.IsVisible = true;
			caption.Text = "Pick a variable to insert it into the text.";
			var panel = NewStep(false);

			var list = new ListBox { ItemsSource = custom.Select(variable => $"{variable.Name} (\"{variable.Value}\")").ToArray(), MinWidth = 240, MaxHeight = 360 };
			list.SelectionChanged += (_, _) =>
			{
				if (list.SelectedIndex is >= 0 and var index)
					Close(custom[index].Name);
			};
			panel.Children.Add(list);
		}

		/// <summary>A full-width button with a title and a dim hint below it, running <paramref name="pick"/> when clicked.</summary>
		private static void Choice(StackPanel panel, string title, string hint, Action pick, bool enabled = true)
		{
			var button = new Button
			{
				Content = new StackPanel
				{
					Spacing = 2,
					Children =
					{
						new TextBlock { Text = title, Foreground = ThemeDefinition.RulerLabel },
						new TextBlock { Text = hint, Foreground = ThemeDefinition.RulerTick, FontSize = 11 },
					},
				},
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Padding = new Thickness(10, 6),
				IsEnabled = enabled,
			};
			button.Click += (_, _) => pick();
			panel.Children.Add(button);
		}

		/// <summary>A cell showing the value the named variable currently produces, naming it in its tooltip;
		/// picking it returns the variable's name.</summary>
		private Button ValueButton(string name)
		{
			var value = values.TryGetValue(name, out var current) ? current : string.Empty;
			var button = new Button
			{
				Content = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240 },
				MinWidth = 56,
				Margin = new Thickness(0, 0, 8, 4),
				Padding = new Thickness(10, 4),
			};
			ToolTip.SetTip(button, name);
			button.Click += (_, _) => Close(name);
			return button;
		}

		/// <summary>Starts a new step sliding in — forward steps from the right, backwards ones from the left —
		/// and returns the panel the step's controls are added to.</summary>
		private StackPanel NewStep(bool backwards)
		{
			stepHost.IsTransitionReversed = backwards;
			var panel = new StackPanel { Spacing = 8 };
			stepHost.Content = panel;
			return panel;
		}

		/// <summary>Returns one step back: from the days or the custom list to the time zones when the category
		/// passed through them, otherwise to the categories.</summary>
		private void GoBack()
		{
			if (depth == 2 && (category == VariableCategory.Date || category == VariableCategory.Time) && conversions.Count > 0)
				ShowTimeZone(category, true);
			else
				ShowCategory(true);
		}
	}
}
