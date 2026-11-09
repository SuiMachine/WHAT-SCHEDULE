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
	private readonly MenuItem replaceImageItem;
	private readonly MenuItem lockItem;
	private SKPoint contextMenuPosition;

	public CanvasMenus(PreviewCanvas canvas)
	{
		this.canvas = canvas;

		bringForwardItem = new MenuItem { Header = "Bring Forward" };
		bringForwardItem.Click += (_, _) => ReorderSelected(1);

		sendBackwardItem = new MenuItem { Header = "Send Backward" };
		sendBackwardItem.Click += (_, _) => ReorderSelected(-1);

		replaceImageItem = new MenuItem { Header = "Replace Image..." };
		replaceImageItem.Click += async (_, _) =>
		{
			if (canvas.Selection.SelectedElement is ImageElement image)
				await ReplaceImageAsync(image);
		};

		lockItem = new MenuItem { Header = "Lock", ToggleType = MenuItemToggleType.CheckBox };
		lockItem.Click += (_, _) =>
		{
			if (canvas.Selection.SelectedElement is { } element && canvas.Document is { } document)
				document.SetLockTracked(element, !element.IsLocked);
		};

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
		replaceImageItem.IsVisible = selected is ImageElement;

		//The lock acts on the selected element, so it is checked to it and disabled without one.
		lockItem.IsChecked = selected is { IsLocked: true };
		lockItem.IsEnabled = selected is not null;
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
		menu.Items.Add(replaceImageItem);
		menu.Items.Add(new Separator());
		menu.Items.Add(bringForwardItem);
		menu.Items.Add(sendBackwardItem);
		menu.Items.Add(new Separator());
		menu.Items.Add(lockItem);
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

		var text = await TextPromptDialog.ShowDialog(window, "Add Label", "Label text", "", document);
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

	/// <summary>
	/// Replaces <paramref name="element"/>'s bitmap with one picked from a file, keeping the element's box: a bitmap whose
	/// aspect ratio matches swaps in uncropped, a taller one is center-cropped off its top and bottom and a wider one off
	/// its left and right sides (with the user warned first, since pixels are lost) so the bitmap fills the box.
	/// </summary>
	public async Task ReplaceImageAsync(ImageElement element)
	{
		var document = canvas.Document;
		if (document is null || !document.ContainsElement(element))
			return;

		if (TopLevel.GetTopLevel(canvas) is not Window window)
			return;

		var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Replace image",
			AllowMultiple = false,
			FileTypeFilter = [FilePickerFileTypes.ImageAll],
		});
		if (files.Count != 1)
			return;

		await using var stream = await files[0].OpenReadAsync();
		var bitmap = SKBitmap.Decode(stream);
		if (bitmap is null)
		{
			await ConfirmationDialog.ShowMessage(window, "Replace Image", "The file could not be loaded as an image.");
			return;
		}

		var fit = element.MeasureBitmapFit(bitmap);
		if (fit != BitmapFit.Same)
		{
			var relation = fit == BitmapFit.Taller ? "taller than" : "wider than";
			var cropped = fit == BitmapFit.Taller ? "from the top and bottom" : "from the left and right sides";
			if (!await ConfirmationDialog.ShowDialog(window, "Replace Image",
				$"The new image is {relation} the image it replaces, so it will be cropped {cropped} to fit the replaced image's box. Replace it anyway?", "Replace"))
			{
				bitmap.Dispose();
				return;
			}
		}

		var replacement = element.CreateReplacement(bitmap);
		document.ReplaceElementTracked(element, replacement);
		canvas.Selection.SelectedElement = replacement;
		canvas.Focus();
		canvas.InvalidateVisual();
	}
}
