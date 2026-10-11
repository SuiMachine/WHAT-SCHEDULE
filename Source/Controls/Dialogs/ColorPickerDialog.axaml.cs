using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SkiaSharp;

namespace WhatSchedule.Controls.Dialogs
{
	public partial class ColorPickerDialog : Window
	{
		private ColorPickerDialog(SKColor initialColor)
		{
			InitializeComponent();
			ColorPickerControl.Color = new Color(initialColor.Red, initialColor.Green, initialColor.Blue, initialColor.Alpha);
		}

		public static async Task<SKColor?> ShowDialog(Window owner, SKColor initialColor)
		{
			var dialog = new ColorPickerDialog(initialColor);
			var result = await dialog.ShowDialog<Color?>(owner);
			if (result is null)
				return initialColor;
			else
				return new SKColor(result.Value.R, result.Value.G, result.Value.B, result.Value.A);
		}

		private void OnOkClick(object? sender, RoutedEventArgs e)
		{
			Close(ColorPickerControl.Color);
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Close(null);
		}
	}
}
