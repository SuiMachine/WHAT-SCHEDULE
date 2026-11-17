// Copyright (C) 2026 Sui_VT
// Licensed under the GNU GPL v3.0 or later - see LICENSE.md or <https://www.gnu.org/licenses/>.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System.Diagnostics;
using WhatSchedule.Controls.Dialogs;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using WhatSchedule.TwitchAPI;
using WhatSchedule.ViewModels;

namespace WhatSchedule;

public partial class MainWindow : Window
{
	private bool closeConfirmed;
	private bool showingClosePrompt;

	public MainWindow()
	{
		InitializeComponent();
		Inspector.Bind(Canvas);
		Hierarchy.Bind(Canvas);
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		if (DataContext is MainWindowViewModel viewModel)
			viewModel.StorageProvider = StorageProvider;

		_ = ShowLegalNoticeOnFirstRunAsync();
	}

	private async Task ShowLegalNoticeOnFirstRunAsync()
	{
		if (FirstRunNotice.WasShown())
			return;

		await AboutDialog.ShowAsync(this);
		FirstRunNotice.MarkShown();
	}

	//The window has no system title bar (WindowDecorations="BorderOnly"), so the custom one in MainWindow.axaml moves,
	//minimizes, maximizes and closes the window instead.

	/// <summary>Starts dragging the window from the custom title bar.</summary>
	private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			BeginMoveDrag(e);
	}

	/// <summary>Maximizes or restores the window when the title bar is double-tapped, ignoring taps on the caption buttons.</summary>
	private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (e.Source is Button)
			return;

		SetMaximized(WindowState != WindowState.Maximized);
	}

	private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

	private void OnMaximizeClick(object? sender, RoutedEventArgs e) => SetMaximized(WindowState != WindowState.Maximized);

	private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

	/// <summary>Applies the menu bar's display filter to the canvas; "None" clears it. Skipped during initialization, when the canvas is not bound yet.</summary>
	private void OnTagFilterChanged(object? sender, SelectionChangedEventArgs e)
	{
		//The XAML's SelectedIndex fires this while InitializeComponent is still loading, before the canvas field is assigned.
		if (Canvas is null)
			return;

		Canvas.DisplayFilter = TagFilter.SelectedIndex switch
		{
			1 => ScheduleFilter.Streaming,
			2 => ScheduleFilter.TakingABreak,
			3 => ScheduleFilter.Final,
			_ => ScheduleFilter.None
		};
	}

	private void SetMaximized(bool maximized) => WindowState = maximized ? WindowState.Maximized : WindowState.Normal;

	/// <summary>Cancels the close and asks the user to confirm it; the window closes only once the user confirms.</summary>
	protected override async void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);
		if (showingClosePrompt)
		{
			//A close requested while the prompt is already open (Alt+F4, the Exit menu item) must not close the window behind the prompt's back.
			e.Cancel = true;
			return;
		}

		//Without recorded history changes there is nothing that could be lost, so the window closes without asking.
		if (closeConfirmed || DataContext is not MainWindowViewModel viewModel || !viewModel.HasHistoryChanges)
			return;

		showingClosePrompt = true;
		e.Cancel = true;
		var confirmed = await ConfirmationDialog.ShowDialog(this, "Exit", "There are unsaved changes. Are you sure you want to close the application?", "Exit");
		showingClosePrompt = false;
		if (confirmed)
		{
			closeConfirmed = true;
			Close();
		}
	}

	private void OnExitClick(object? sender, EventArgs e) => Close();

	private async void OnAboutClick(object? sender, EventArgs e) => await AboutDialog.ShowAsync(this);

	private async void OnIssuesTrackerClick(object? sender, EventArgs e)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = "https://github.com/SuiMachine/WHAT-SCHEDULE/issues",
			UseShellExecute = true
		});
	}	

	//The picked week and its date culture land on the document, so the days' date variables follow the selection.
	private async void OnSelectWeekClick(object? sender, EventArgs e)
	{
		if (DataContext is not MainWindowViewModel viewModel)
			return;

		if (await WeekPickerDialog.PickWeek(this, viewModel.Document.Week) is { } selection)
		{
			viewModel.Document.Week.WeekStart = selection.Week.Start;
			viewModel.Document.Week.DateCulture = selection.DateCulture;
			viewModel.Document.RaiseChanged();
		}
	}

	//Only the editor's OK applies the week and the variables to the document; Cancel keeps the previous ones.
	private async void OnEditScheduleClick(object? sender, EventArgs e)
	{
		if (DataContext is not MainWindowViewModel viewModel)
			return;

		if (await ScheduleEditorDialog.EditSchedule(this, viewModel.Document.Week, viewModel.Document.Variables) is { } edit)
		{
			viewModel.Document.Week = edit.Week;
			viewModel.Document.Variables = edit.Variables;
			viewModel.Document.RaiseChanged();
		}
	}

	private async void OnImportScheduleFromTwitchClick(object? sender, EventArgs e)
	{
		if (DataContext is not MainWindowViewModel viewModel)
			return;

		if (viewModel.Document.Week.WeekStart == null)
			return;

		var weekStart = viewModel.Document.Week.WeekStart.GetValueOrDefault();

		if (!await ConfirmationDialog.ShowDialog(this, "Import schedule from Twitch...", $"Importing a schedule will delete current week information. Are you sure you want to continue?\n\nThe import will be for: {weekStart} - {weekStart.AddDays(6)}. If this is incorrect, edit week selection.", "Import"))
			return;

		var import = await TwitchScheduleImporter.ImportScheduleFromTwitchAsync(this, weekStart);
		if (import != null && import.Length > 0)
		{
			foreach (var twitchStream in import)
			{
				var doc = viewModel.Document.Week.TimeZoneId;

				if (doc == null)
				{
					twitchStream.start_time = twitchStream.start_time?.ToLocalTime();
					twitchStream.end_time = twitchStream.end_time?.ToLocalTime();
				}
				else
				{
					twitchStream.start_time = TimeZoneInfo.ConvertTimeFromUtc(twitchStream.start_time.GetValueOrDefault().ToUniversalTime(), TimeZoneInfo.FindSystemTimeZoneById(doc));
					twitchStream.end_time = TimeZoneInfo.ConvertTimeFromUtc(twitchStream.end_time.GetValueOrDefault().ToUniversalTime(), TimeZoneInfo.FindSystemTimeZoneById(doc));
				}
			}

			viewModel.Document.History.Clear();
			int dayOffset = 0;
			foreach (var day in viewModel.Document.Week.Days)
			{
				var dayToCheck = weekStart.AddDays(dayOffset);
				var find = import.FirstOrDefault(x => DateOnly.FromDateTime(x.start_time.GetValueOrDefault()) == dayToCheck);
				if(find == null)
					day.IsStreaming = false;
				else
				{
					day.StreamTime = TimeOnly.FromDateTime(find.start_time.GetValueOrDefault());
					day.StreamTitle = find.title ?? string.Empty;
					day.IsStreaming = true;
				}
				dayOffset++;
			}
			viewModel.Document.RaiseChanged();
		}
	}

	//Added elements land in the top left corner of the document, since a menu click carries no canvas position.

	private async void OnAddLabelClick(object? sender, EventArgs e) => await Canvas.Menus.AddLabelAsync(new SKPoint(0, 0));

	private async void OnAddImageClick(object? sender, EventArgs e) => await Canvas.Menus.AddImageAsync(new SKPoint(0, 0));

	//A Photoshop import replaces the whole project: the file's background layer becomes the document background, its size the document's size, and its layers the only elements.
	private async void OnImportPsdClick(object? sender, EventArgs e)
	{
		if (DataContext is not MainWindowViewModel viewModel)
			return;

		if (!await ConfirmationDialog.ShowDialog(this, "Import PSD", "Importing a Photoshop document will remove all of the content of the current canvas. The importer doesn't preserve complex effects and layering of PSD files, so it's best to squash your layers when possible before import. Continue?", "Import"))
			return;

		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Import Photoshop document",
			AllowMultiple = false,
			FileTypeFilter = [new FilePickerFileType("Photoshop document") { Patterns = ["*.psd"] }],
		});
		if (files.Count != 1)
			return;

		PsdImportResult import;
		await using (var stream = await files[0].OpenReadAsync())
		{
			try
			{
				import = PsdImporter.Import(stream);
			}
			catch (Exception error) when (error is InvalidDataException or ArgumentException)
			{
				await ConfirmationDialog.ShowMessage(this, "Import PSD", "The Photoshop file could not be imported: " + error.Message);
				return;
			}
		}

		viewModel.Document.PrepareNewWithBackground(import.Background);
		if (import.Elements.Count > 0)
			viewModel.Document.AddRangeTracked(import.Elements, "Import PSD");
	}

	//The clipboard is internal to the canvas; the menu items cover focus it loses to other controls.

	private void OnCutClick(object? sender, EventArgs e) => Canvas.CutSelection();

	private void OnCopyClick(object? sender, EventArgs e) => Canvas.CopySelection();

	private void OnPasteClick(object? sender, EventArgs e) => Canvas.PasteClipboard();
}
