using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using WhatSchedule.Documents;
using Calendar = Avalonia.Controls.Calendar;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>A contiguous range of dates; the week picker returns the Monday–Sunday week containing the picked date.</summary>
	public readonly record struct DateSegment(DateOnly Start, DateOnly End)
	{
		/// <summary>The Monday–Sunday week containing the given date.</summary>
		public static DateSegment WeekOf(DateOnly date)
		{
			var start = date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
			return new DateSegment(start, start.AddDays(6));
		}
	}

	/// <summary>The week selection the picker returns: the week's dates and the culture they are displayed with.</summary>
	public readonly record struct WeekSelection(DateSegment Week, ScheduleDateCulture DateCulture);

	/// <summary>A picker that shows a month calendar and highlights the whole Monday–Sunday week of any day the user clicks,
	/// with the culture the week's dates are displayed as text picked alongside it.</summary>
	public sealed class WeekPickerDialog : Window
	{
		private readonly Calendar calendar = new()
		{
			SelectionMode = CalendarSelectionMode.MultipleRange,
			FirstDayOfWeek = DayOfWeek.Monday,
		};

		private readonly TextBlock weekRange = new();
		private readonly Button okButton = new() { Content = "OK", IsDefault = true };

		/// <summary>Picks the culture the week's dates are displayed with, e.g. "Feb 3" (local) or "Feb 3" (en-US) on a Polish system.</summary>
		private readonly ComboBox dateFormatBox = new()
		{
			ItemsSource = new[] { "Local culture", "English (US)" },
		};

		/// <summary>The Monday of the week currently selected on the calendar, or null when the selection is empty.</summary>
		private DateOnly? selectedWeekStart;

		/// <summary>The seven dates currently highlighted, so a click can be told apart from the selection the picker itself sets.</summary>
		private readonly List<DateTime> displayedWeek = [];

		/// <summary>True while the picker sets the week selection itself; the calendar's change handler ignores changes made then.</summary>
		private bool syncingSelection;

		private WeekPickerDialog(ScheduleWeek week)
		{
			Title = "Select Week";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			Background = ThemeDefinition.Background;

			var cancelButton = new Button { Content = "Cancel", IsCancel = true };
			okButton.Click += (_, _) =>
			{
				if (selectedWeekStart is { } start)
					Close(new WeekSelection(new DateSegment(start, start.AddDays(6)), dateFormatBox.SelectedIndex == 1 ? ScheduleDateCulture.EnUs : ScheduleDateCulture.Local));
			};
			cancelButton.Click += (_, _) => Close(null);

			dateFormatBox.SelectedIndex = week.DateCulture == ScheduleDateCulture.EnUs ? 1 : 0;
			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 12,
				Children =
				{
					calendar,
					weekRange,
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 6,
						Children =
						{
							new TextBlock { Text = "Date format:", VerticalAlignment = VerticalAlignment.Center },
							dateFormatBox,
						},
					},
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 8,
						HorizontalAlignment = HorizontalAlignment.Right,
						Children = { okButton, cancelButton },
					},
				},
			};

			calendar.SelectedDatesChanged += (_, _) => OnCalendarSelectionChanged();
			SelectWeek(week.WeekStart ?? DateOnly.FromDateTime(DateTime.Now));
		}

		/// <summary>Shows the picker with the given week's selection and date culture; returns the picked week and culture, or null when the user cancels.</summary>
		public static async Task<WeekSelection?> PickWeek(Window owner, ScheduleWeek week)
		{
			var dialog = new WeekPickerDialog(week);
			return await dialog.ShowDialog<WeekSelection?>(owner);
		}

		/// <summary>Re-selects the whole week of the day a user click introduced; only the calendar's own changes arrive here.</summary>
		private void OnCalendarSelectionChanged()
		{
			if (syncingSelection)
				return;

			var selected = calendar.SelectedDates;
			//A user click leaves a partial selection (the one day clicked, or a dragged or ctrl-added range). The new week is
			//picked by the first selected date outside the week already shown, falling back to the earliest selected date.
			var anchor = selected.Except(displayedWeek).OrderBy(date => date).FirstOrDefault();
			if (anchor == default)
				anchor = selected.OrderBy(date => date).FirstOrDefault();

			if (anchor == default)
			{
				selectedWeekStart = null;
				displayedWeek.Clear();
				weekRange.Text = "Pick a date to choose its week.";
				okButton.IsEnabled = false;
				return;
			}

			SelectWeek(DateOnly.FromDateTime(anchor));
		}

		/// <summary>Highlights all seven days of the given date's week and records it as the picked week.</summary>
		private void SelectWeek(DateOnly date)
		{
			var week = DateSegment.WeekOf(date);
			selectedWeekStart = week.Start;
			displayedWeek.Clear();
			for (var day = week.Start; day <= week.End; day = day.AddDays(1))
				displayedWeek.Add(day.ToDateTime(TimeOnly.MinValue));

			syncingSelection = true;
			calendar.SelectedDates.Clear();
			foreach (var day in displayedWeek)
				calendar.SelectedDates.Add(day);
			syncingSelection = false;
			//A week preselected from the document can lie in another month than the one the calendar opens on.
			calendar.DisplayDate = week.Start.ToDateTime(TimeOnly.MinValue);

			weekRange.Text = string.Create(CultureInfo.CurrentCulture, $"{week.Start:ddd yyyy-MM-dd} – {week.End:ddd yyyy-MM-dd}");
			okButton.IsEnabled = true;
		}
	}
}
