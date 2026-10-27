using Avalonia.Controls;
using Avalonia.Layout;
using WhatSchedule.Documents;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>A list of the schedule's defined variables; picking one returns its name for the caller to insert.</summary>
	public sealed class VariableListDialog : Window
	{
		private VariableListDialog(IReadOnlyList<ScheduleVariable> variables)
		{
			Title = "Variables";
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			Background = ThemeDefinition.Background;

			//The window follows the content, so the list alone keeps growing lists in place.
			var list = new ListBox { ItemsSource = variables.Select(Format).ToArray(), MinWidth = 240, MaxHeight = 360 };
			list.SelectionChanged += (_, _) =>
			{
				if (list.SelectedIndex is >= 0 and var index)
					Close(variables[index].Name);
			};

			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 8,
				Children =
				{
					new TextBlock { Text = "Pick a variable to insert it into the text.", Foreground = ThemeDefinition.RulerTick },
					list,
				},
			};
		}

		/// <summary>Shows the variables; returns the picked variable's name, or null when nothing was picked.</summary>
		public static async Task<string?> PickVariable(Window owner, IReadOnlyList<ScheduleVariable> variables)
		{
			var dialog = new VariableListDialog(variables);
			return await dialog.ShowDialog<string?>(owner);
		}

		/// <summary>The variable as shown in the list: its name, with the value it currently produces in quotes behind it.</summary>
		private static string Format(ScheduleVariable variable) => $"{variable.Name} (\"{variable.Value ?? string.Empty}\")";
	}
}
