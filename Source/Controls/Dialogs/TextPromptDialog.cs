using Avalonia.Controls;
using Avalonia.Layout;
using WhatSchedule.Documents;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>A single-line text prompt; with variables it also offers a button inserting the reference of the one the user picks.</summary>
	public sealed class TextPromptDialog : Window
	{
		private readonly TextBox textBox;

		private TextPromptDialog(string title, string caption, string defaultText, IReadOnlyList<ScheduleVariable>? variables)
		{
			Title = title;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			SizeToContent = SizeToContent.WidthAndHeight;
			CanResize = false;
			ShowInTaskbar = false;
			MinWidth = 360;
			Background = ThemeDefinition.Background;

			textBox = new TextBox
			{
				Text = defaultText,
				PlaceholderText = caption,
				Width = 320,
			};

			var okButton = new Button { Content = "OK", IsDefault = true };
			var cancelButton = new Button { Content = "Cancel", IsCancel = true };
			okButton.Click += (_, _) => Close(textBox.Text);
			cancelButton.Click += (_, _) => Close(null);

			var buttons = new DockPanel();
			if (variables is { Count: > 0 })
			{
				//The picked variable's reference lands in the text at the caret, so it can be placed between typed words.
				var useVariableButton = new Button { Content = "Use variable" };
				useVariableButton.Click += async (_, _) =>
				{
					if (await VariableListDialog.PickVariable(this, variables) is { } name)
						InsertVariable("{" + name + "}");
				};
				DockPanel.SetDock(useVariableButton, Dock.Left);
				buttons.Children.Add(useVariableButton);
			}

			//The OK/Cancel pair fills the row, right-aligned inside it.
			buttons.Children.Add(new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8,
				HorizontalAlignment = HorizontalAlignment.Right,
				Children = { okButton, cancelButton },
			});

			Content = new StackPanel
			{
				Margin = new Avalonia.Thickness(12),
				Spacing = 12,
				Children = { textBox, buttons },
			};

			Opened += (_, _) => textBox.Focus();
		}

		/// <summary>Asks the user for a single line of text; returns null when the user cancels.
		/// With <paramref name="variables"/>, a "Use variable" button inserts the reference of the picked one at the caret.</summary>
		public static async Task<string?> ShowDialog(Window owner, string title, string caption, string defaultText = "", IReadOnlyList<ScheduleVariable>? variables = null)
		{
			var dialog = new TextPromptDialog(title, caption, defaultText, variables);
			return await dialog.ShowDialog<string?>(owner);
		}

		/// <summary>Inserts the reference into the text at the caret and puts the caret behind it.</summary>
		private void InsertVariable(string reference)
		{
			var text = textBox.Text ?? string.Empty;
			var caret = Math.Clamp(textBox.CaretIndex, 0, text.Length);
			textBox.Text = text.Insert(caret, reference);
			textBox.CaretIndex = caret + reference.Length;
			textBox.Focus();
		}
	}
}
