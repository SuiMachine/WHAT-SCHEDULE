// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WhatSchedule.Controls.Dialogs
{
	public sealed class ConfirmationDialog : Window
	{
		private ConfirmationDialog(string title, string message, string confirmText, bool showCancel)
		{
			Title = title;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			MinWidth = 360;
			Background = ThemeDefinition.Background;

			var confirmButton = new Button { Content = confirmText, IsDefault = true };
			confirmButton.Click += (_, _) => Close(true);

			var buttons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			if (showCancel)
			{
				var cancelButton = new Button { Content = "Cancel", IsCancel = true };
				cancelButton.Click += (_, _) => Close(false);
				buttons.Children.Add(cancelButton);
			}
			buttons.Children.Add(confirmButton);

			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 12,
				Children =
				{
					new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Width = 320 },
					buttons,
				},
			};
		}

		/// <summary>Asks the user to confirm an action; returns true only when the user confirms it.</summary>
		public static async Task<bool> ShowDialog(Window owner, string title, string message, string confirmText)
		{
			var dialog = new ConfirmationDialog(title, message, confirmText, showCancel: true);
			return await dialog.ShowDialog<bool>(owner);
		}

		/// <summary>Shows a message the user can only acknowledge.</summary>
		public static async Task ShowMessage(Window owner, string title, string message)
		{
			var dialog = new ConfirmationDialog(title, message, "OK", showCancel: false);
			_ = await dialog.ShowDialog<bool>(owner);
		}
	}
}
