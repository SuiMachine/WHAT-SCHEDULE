// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WhatSchedule.Controls.Dialogs
{
	public partial class AboutDialog : Window
	{
		private const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

		public AboutDialog()
		{
			InitializeComponent();
		}

		public static async Task ShowAsync(Window owner)
		{
			var dialog = new AboutDialog();
			_ = await dialog.ShowDialog<object?>(owner);
		}

		private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

		private void OnViewLicenseClick(object? sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo { FileName = LicenseUrl, UseShellExecute = true });

		//Opens the link stored in the clicked button's Tag.
		private void OnCreditLinkClick(object? sender, RoutedEventArgs e)
		{
			if ((sender as Button)?.Tag is string url)
				Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
		}
	}
}
