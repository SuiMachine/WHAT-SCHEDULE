using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using WhatSchedule.Controls.Dialogs;
using WhatSchedule.Documents;
using WhatSchedule.Elements;

namespace WhatSchedule.Controls.ContextMenus;

/// <summary>The ruler and canvas context menus, including the add-element dialogs they open.</summary>
internal sealed class CanvasMenus
{
	private readonly PreviewCanvas canvas;
	private readonly ContextMenu rulerMenu;
	private readonly ContextMenu canvasMenu;
	private readonly MenuItem bringForwardItem;
	private readonly MenuItem sendBackwardItem;
	private SKPoint contextMenuPosition;

	public CanvasMenus(PreviewCanvas canvas)
	{
		this.canvas = canvas;

		bringForwardItem = new MenuItem { Header = "Bring Forward" };
		bringForwardItem.Click += (_, _) => ReorderSelected(1);

		sendBackwardItem = new MenuItem { Header = "Send Backward" };
		sendBackwardItem.Click += (_, _) => ReorderSelected(-1);

		rulerMenu = BuildRulerMenu();
		canvasMenu = BuildCanvasMenu();
	}

	/// <summary>Opens the ruler unit menu when <paramref name="position"/> lies on a ruler strip.</summary>
	public bool ShowRulerMenu(Point position)
	{
		var horizontal = position.X >= ThemeDefinition.RulerThickness && position.Y < ThemeDefinition.RulerThickness;
		var vertical = position.X < ThemeDefinition.RulerThickness && position.Y >= ThemeDefinition.RulerThickness;
		if (!horizontal && !vertical)
			return false;

		for (var i = 0; i < rulerMenu.Items.Count; i++)
		{
			if (rulerMenu.Items[i] is not MenuItem item)
				continue;
			item.IsChecked = canvas.Rulers.Unit == (RulerRenderer.RulerUnit)i;
		}

		Open(rulerMenu);
		return true;
	}

	/// <summary>Opens the add-element menu, remembering the document position it was opened at.</summary>
	public bool ShowCanvasMenu(Point position)
	{
		var document = canvas.Document;
		if (document is not { Width: > 0, Height: > 0 } || canvas.Surface.Bitmap is null)
			return false;

		UpdateLayerMenuItems(document);
		contextMenuPosition = canvas.Viewport.ScreenToDocument(position, canvas.GetImageRect(document));
		Open(canvasMenu);
		return true;
	}

	/// <summary>Enables the layering items when the selected element can actually move in that direction.</summary>
	private void UpdateLayerMenuItems(Document document)
	{
		var selected = canvas.Selection.SelectedElement;
		var elements = selected is not null ? document.SiblingsOf(selected) : null;
		var index = -1;
		for (var i = 0; elements is not null && i < elements.Count; i++)
		{
			if (ReferenceEquals(elements[i], selected))
			{
				index = i;
				break;
			}
		}

		bringForwardItem.IsEnabled = index >= 0 && index < elements!.Count - 1;
		sendBackwardItem.IsEnabled = index > 0;
	}

	private void Open(ContextMenu menu)
	{
		//A stupid hack because if you right click and call open, the open will run before the previous closing
		//because of some stupid nonsense Avalonia does in background
		//Don't question - it's a small detail, but makes it feel so much nicer
		if (menu.IsOpen)
		{
			menu.Close();
			Dispatcher.UIThread.Post(() => menu.Open(canvas));
		}
		else
			menu.Open(canvas);
	}

	private ContextMenu BuildRulerMenu()
	{
		var menu = new ContextMenu();

		foreach (var unit in Enum.GetValues<RulerRenderer.RulerUnit>())
		{
			var item = new MenuItem
			{
				Header = unit.ToString(),
				ToggleType = MenuItemToggleType.Radio,
			};
			item.Click += (_, _) =>
			{
				canvas.Rulers.Unit = unit;
				canvas.InvalidateVisual();
			};
			menu.Items.Add(item);
		}

		return menu;
	}

	private ContextMenu BuildCanvasMenu()
	{
		var menu = new ContextMenu();

		var addLabel = new MenuItem { Header = "Add Label..." };
		addLabel.Click += async (_, _) => await AddLabelAsync(contextMenuPosition);

		var addImage = new MenuItem { Header = "Add Image..." };
		addImage.Click += async (_, _) => await AddImageAsync(contextMenuPosition);

		menu.Items.Add(addLabel);
		menu.Items.Add(addImage);
		menu.Items.Add(new Separator());
		menu.Items.Add(bringForwardItem);
		menu.Items.Add(sendBackwardItem);
		return menu;
	}

	/// <summary>Moves the selected element in the layering order by <paramref name="offset"/> steps.</summary>
	private void ReorderSelected(int offset)
	{
		if (canvas.Selection.SelectedElement is { } element && canvas.Document is { } document)
			document.ReorderTracked(element, offset);
	}

	public async Task AddLabelAsync(SKPoint position)
	{
		var document = canvas.Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		if (TopLevel.GetTopLevel(canvas) is not Window window)
			return;

		var text = await TextPromptDialog.ShowDialog(window, "Add Label", "Label text", "", ScheduleVariables.Collect(document));
		if (string.IsNullOrWhiteSpace(text))
			return;

		var element = new LabelElement { Text = text.Trim(), Position = position };
		document.AddTracked(element);
		canvas.Selection.SelectedElement = element;
		canvas.Focus();
		canvas.InvalidateVisual();
	}

	public async Task AddImageAsync(SKPoint position)
	{
		var document = canvas.Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		var topLevel = TopLevel.GetTopLevel(canvas);
		if (topLevel is null)
			return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Add image",
			AllowMultiple = false,
			FileTypeFilter = [FilePickerFileTypes.ImageAll],
		});
		if (files.Count != 1)
			return;

		await using var stream = await files[0].OpenReadAsync();
		var bitmap = SKBitmap.Decode(stream);
		if (bitmap is null)
			return;

		document.AddTracked(new ImageElement(bitmap, position));
	}
}
