using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
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

	/// <summary>A picker that shows a month calendar and highlights the whole Monday–Sunday week of any day the user clicks.</summary>
	public sealed class WeekPickerDialog : Window
	{
		private readonly Calendar calendar = new()
		{
			SelectionMode = CalendarSelectionMode.MultipleRange,
			FirstDayOfWeek = DayOfWeek.Monday,
		};

		private readonly TextBlock weekRange = new();
		private readonly Button okButton = new() { Content = "OK", IsDefault = true };

		/// <summary>The Monday of the week currently selected on the calendar, or null when the selection is empty.</summary>
		private DateOnly? selectedWeekStart;

		/// <summary>The seven dates currently highlighted, so a click can be told apart from the selection the picker itself sets.</summary>
		private readonly List<DateTime> displayedWeek = [];

		/// <summary>True while the picker sets the week selection itself; the calendar's change handler ignores changes made then.</summary>
		private bool syncingSelection;

		private WeekPickerDialog()
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
					Close(new DateSegment(start, start.AddDays(6)));
			};
			cancelButton.Click += (_, _) => Close(null);

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
						Spacing = 8,
						HorizontalAlignment = HorizontalAlignment.Right,
						Children = { okButton, cancelButton },
					},
				},
			};

			calendar.SelectedDatesChanged += (_, _) => OnCalendarSelectionChanged();
			SelectWeek(DateOnly.FromDateTime(DateTime.Now));
		}

		/// <summary>Shows the picker and returns the Monday–Sunday week of the picked date, or null when the user cancels.</summary>
		public static async Task<DateSegment?> PickWeek(Window owner)
		{
			var dialog = new WeekPickerDialog();
			return await dialog.ShowDialog<DateSegment?>(owner);
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

			weekRange.Text = string.Create(CultureInfo.CurrentCulture, $"{week.Start:ddd yyyy-MM-dd} – {week.End:ddd yyyy-MM-dd}");
			okButton.IsEnabled = true;
		}
	}
}
