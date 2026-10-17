using Avalonia.Controls;
using WhatSchedule.Controls.Dialogs;
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
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		if (DataContext is MainWindowViewModel viewModel)
			viewModel.StorageProvider = StorageProvider;
	}

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
}
