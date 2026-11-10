using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SkiaSharp;

namespace WhatSchedule.Controls.Dialogs
{
	/// <summary>What the user chose in the color dialog: OK with the color, a request to sample one from the canvas, or a plain cancel.</summary>
	public enum ColorPickOutcome
	{
		Picked,
		Sample,
		Cancelled,
	}

	/// <summary>
	/// The color editor dialog. The Sample button closes the dialog asking for a canvas sample; the caller
	/// then samples the canvas and reopens the editor with the drawn color, or reopens it unchanged when the
	/// sampling was cancelled.
	/// </summary>
	public partial class ColorPickerDialog : Window
	{
		public ColorPickerDialog()
		{
			InitializeComponent();
		}

		private ColorPickerDialog(SKColor initialColor)
		{
			InitializeComponent();
			ColorPickerControl.Color = new Color(initialColor.Alpha, initialColor.Red, initialColor.Green, initialColor.Blue);
			Background = ThemeDefinition.Background;
		}

		/// <summary>Opens the color editor; returns what the user chose, with the color for the picked outcome.</summary>
		public static async Task<(ColorPickOutcome Outcome, SKColor Color)> ShowDialog(Window owner, SKColor initialColor)
		{
			var dialog = new ColorPickerDialog(initialColor);
			var outcome = await dialog.ShowDialog<ColorPickOutcome?>(owner);
			if (outcome is null)
				return (ColorPickOutcome.Cancelled, initialColor);

			//The dialog's editor outlives the close, so OK still reads the color the user left there.
			var edited = dialog.ColorPickerControl.Color;
			return (outcome.Value, new SKColor(edited.R, edited.G, edited.B, edited.A));
		}

		private void OnOkClick(object? sender, RoutedEventArgs e)
		{
			Close(ColorPickOutcome.Picked);
		}

		private void OnSampleClick(object? sender, RoutedEventArgs e)
		{
			Close(ColorPickOutcome.Sample);
		}

		private void OnCancelClick(object? sender, RoutedEventArgs e)
		{
			Close(null);
		}
	}
}
