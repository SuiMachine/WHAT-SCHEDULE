using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WhatSchedule.Controls.Dialogs
{
	public sealed class ConfirmationDialog : Window
	{
		private ConfirmationDialog(string title, string message, string confirmText)
		{
			Title = title;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			MinWidth = 360;
			Background = ThemeDefinition.Background;

			var cancelButton = new Button { Content = "Cancel", IsCancel = true };
			var confirmButton = new Button { Content = confirmText, IsDefault = true };
			cancelButton.Click += (_, _) => Close(false);
			confirmButton.Click += (_, _) => Close(true);

			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 12,
				Children =
				{
					new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Width = 320 },
					new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 8,
						HorizontalAlignment = HorizontalAlignment.Right,
						Children = { cancelButton, confirmButton },
					},
				},
			};
		}

		/// <summary>Asks the user to confirm an action; returns true only when the user confirms it.</summary>
		public static async Task<bool> ShowDialog(Window owner, string title, string message, string confirmText)
		{
			var dialog = new ConfirmationDialog(title, message, confirmText);
			return await dialog.ShowDialog<bool>(owner);
		}
	}
}
