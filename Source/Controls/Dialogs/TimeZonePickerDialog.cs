using Avalonia.Controls;
using Avalonia.Layout;
using WhatSchedule.Documents;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>A zone picked from the list: its portable ID, null meaning the system's local zone.</summary>
	public readonly record struct ZoneSelection(string? TimeZoneId);

	/// <summary>A searchable list of the system's time zones; picking one returns it, or null when the list is dismissed.</summary>
	public sealed class TimeZonePickerDialog : Window
	{
		/// <summary>The zones the list offers: the system's local zone first, then every zone the system knows, as the system shows them.</summary>
		private static readonly (string? Id, string Text)[] Zones =
		[
			(null, "System local"),
			.. TimeZoneInfo.GetSystemTimeZones().Select(zone => (ScheduleZones.PortableId(zone), zone.DisplayName)),
		];

		/// <summary>The zones the current search shows; the list's selected index refers into this.</summary>
		private (string? Id, string Text)[] shown = Zones;

		private TimeZonePickerDialog()
		{
			Title = "Pick time zone";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			Background = ThemeDefinition.Background;

			var search = new TextBox { PlaceholderText = "Search" };
			var list = new ListBox { ItemsSource = Names(shown), MinWidth = 320, MaxHeight = 360 };

			//The search narrows the list by display name or ID, e.g. "warsaw" or "Europe/Warsaw".
			search.TextChanged += (_, _) =>
			{
				shown = Zones.Where(zone => Matches(zone, (search.Text ?? "").Trim())).ToArray();
				list.ItemsSource = Names(shown);
			};
			list.SelectionChanged += (_, _) =>
			{
				if (list.SelectedIndex is >= 0 and var index)
					Close(new ZoneSelection(shown[index].Id));
			};

			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 8,
				Children =
				{
					new TextBlock { Text = "Pick a time zone.", Foreground = ThemeDefinition.RulerTick },
					search,
					list,
				},
			};
		}

		/// <summary>Shows the zone list; returns the picked zone, or null when nothing was picked.</summary>
		public static async Task<ZoneSelection?> PickZone(Window owner)
		{
			var dialog = new TimeZonePickerDialog();
			return await dialog.ShowDialog<ZoneSelection?>(owner);
		}

		/// <summary>The zones' display names, as the list shows them.</summary>
		private static string[] Names((string? Id, string Text)[] zones) => [.. zones.Select(zone => zone.Text)];

		/// <summary>Whether a zone matches the search: its display name or its ID contains the search text, ignoring case.</summary>
		private static bool Matches((string? Id, string Text) zone, string filter) =>
			filter.Length == 0
			|| zone.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)
			|| (zone.Id ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase);
	}
}
