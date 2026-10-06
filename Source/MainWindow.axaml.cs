using Avalonia.Controls;
using WhatSchedule.ViewModels;

namespace WhatSchedule;

public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();

	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		var mainWindowModel = this.DataContext as MainWindowViewModel;
		mainWindowModel!.RenderPreview();

	}
}
