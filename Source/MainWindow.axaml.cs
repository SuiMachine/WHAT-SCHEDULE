using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SkiaSharp;
using WhatSchedule.Controls.Dialogs;
using WhatSchedule.Elements;
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

	/// <summary>Applies the menu bar's activity filter to the canvas display; "None" clears it. Skipped during initialization, when the canvas is not bound yet.</summary>
	private void OnTagFilterChanged(object? sender, SelectionChangedEventArgs e)
	{
		//The XAML's SelectedIndex fires this while InitializeComponent is still loading, before the canvas field is assigned.
		if (Canvas is null)
			return;

		Canvas.TagFilter = TagFilter.SelectedIndex switch
		{
			1 => ScheduleActivity.Streaming,
			2 => ScheduleActivity.TakingABreak,
			_ => null
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

	//Added elements land in the top left corner of the document, since a menu click carries no canvas position.

	private async void OnAddLabelClick(object? sender, EventArgs e) => await Canvas.Menus.AddLabelAsync(new SKPoint(0, 0));

	private async void OnAddImageClick(object? sender, EventArgs e) => await Canvas.Menus.AddImageAsync(new SKPoint(0, 0));

	//The clipboard is internal to the canvas; the menu items cover focus it loses to other controls.

	private void OnCutClick(object? sender, EventArgs e) => Canvas.CutSelection();

	private void OnCopyClick(object? sender, EventArgs e) => Canvas.CopySelection();

	private void OnPasteClick(object? sender, EventArgs e) => Canvas.PasteClipboard();
}
