using Avalonia.Controls;
using WhatSchedule.ViewModels;

namespace WhatSchedule;

public partial class MainWindow : Window
{
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
}
