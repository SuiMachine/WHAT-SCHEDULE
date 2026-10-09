using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;
using System.Globalization;
using WhatSchedule.Documents;
using WhatSchedule.Elements;
using WhatSchedule.Rendering;

namespace WhatSchedule.Controls;

public class PreviewCanvas : Control, IDisposable
{
	private enum DragMode
	{
		None,
		Pan,
		MoveElement,
		ScaleElement,
	}

	private enum Corner
	{
		TopLeft,
		TopRight,
		BottomRight,
		BottomLeft,
	}

	private static readonly Corner[] Corners = { Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft };

	private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
	private static readonly Cursor TopLeftCursor = new(StandardCursorType.TopLeftCorner);
	private static readonly Cursor TopRightCursor = new(StandardCursorType.TopRightCorner);
	private static readonly Cursor BottomRightCursor = new(StandardCursorType.BottomRightCorner);
	private static readonly Cursor BottomLeftCursor = new(StandardCursorType.BottomLeftCorner);

	private readonly FormattedTextCache labelCache = new();
	private ContextMenu rulerContextMenu;
	private ContextMenu canvasContextMenu;

	private double scale = 1;
	private double offsetX;
	private double offsetY;
	private bool hasInitialFit;
	private RulerUnit currentRulerUnit = RulerUnit.Pixels;

	private WriteableBitmap? previewBitmap;
	private bool previewDirty = true;

	private DragMode dragMode = DragMode.None;
	private Point dragStartPosition;
	private double dragStartOffsetX;
	private double dragStartOffsetY;

	private CanvasElement? selectedElement;
	private SKPoint moveStartPosition;
	private SKPoint scaleAnchor;
	private CanvasElement.ScaleState? scaleStart;

	private SKPoint contextMenuPosition;

	public static readonly StyledProperty<Document?> DocumentProperty = AvaloniaProperty.Register<PreviewCanvas, Document?>(nameof(Document));

	public Document? Document
	{
		get => GetValue(DocumentProperty);
		set => SetValue(DocumentProperty, value);
	}

	public PreviewCanvas()
	{
		PointerCaptureLost += (_, _) =>
		{
			dragMode = DragMode.None;
			Cursor = Cursor.Default;
		};
		rulerContextMenu = BuildRulerContextMenu();
		canvasContextMenu = BuildCanvasContextMenu();
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property == DocumentProperty)
		{
			if (change.OldValue is Document oldDocument)
				oldDocument.Changed -= OnDocumentChanged;
			if (change.NewValue is Document newDocument)
				newDocument.Changed += OnDocumentChanged;

			previewBitmap?.Dispose();
			previewBitmap = null;
			previewDirty = true;
			hasInitialFit = false;
			selectedElement = null;
			dragMode = DragMode.None;
			InvalidateVisual();
		}
	}

	private void OnDocumentChanged()
	{
		previewDirty = true;
		InvalidateVisual();
	}

	public void Dispose()
	{
		if (Document is { } document)
			document.Changed -= OnDocumentChanged;

		previewBitmap?.Dispose();
		previewBitmap = null;
		GC.SuppressFinalize(this);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);

		var point = e.GetCurrentPoint(this);
		if (point.Properties.IsRightButtonPressed)
		{
			if (ShowRulerContextMenu(point.Position) || ShowCanvasContextMenu(point.Position))
			{
				e.Handled = true;
			}

			return;
		}

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0 || !point.Properties.IsLeftButtonPressed)
			return;

		var position = e.GetPosition(this);

		if (selectedElement is not null && HitTestHandle(position) is { } corner)
		{
			BeginScale(corner);
			e.Pointer.Capture(this);
			e.Handled = true;
			return;
		}

		if (HitTestElement(position) is { } element)
		{
			selectedElement = element;
			dragMode = DragMode.MoveElement;
			dragStartPosition = position;
			moveStartPosition = element.Position;
			Cursor = MoveCursor;
			e.Pointer.Capture(this);
			InvalidateVisual();
			e.Handled = true;
			return;
		}

		if (selectedElement is not null)
		{
			selectedElement = null;
			InvalidateVisual();
		}

		dragMode = DragMode.Pan;
		dragStartPosition = position;
		dragStartOffsetX = offsetX;
		dragStartOffsetY = offsetY;
		Cursor = MoveCursor;
		e.Pointer.Capture(this);
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);

		var position = e.GetPosition(this);

		switch (dragMode)
		{
			case DragMode.None:
				UpdateHoverCursor(position);
				return;
			case DragMode.Pan:
				MovePan(position);
				return;
			case DragMode.MoveElement:
				MoveElement(position);
				return;
			case DragMode.ScaleElement:
				ScaleElement(position, e);
				return;
		}
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		if (e.Properties.IsRightButtonPressed)
			return;
		base.OnPointerReleased(e);
		EndDrag(e.Pointer);
		e.Handled = true;
	}

	private void EndDrag(IPointer pointer)
	{
		if (dragMode == DragMode.None)
			return;

		dragMode = DragMode.None;
		if (pointer.Captured == this)
		{
			pointer.Capture(null);
		}

		Cursor = Cursor.Default;
	}

	private void MovePan(Point position)
	{
		var document = Document;
		if (document is null)
			return;

		offsetX = dragStartOffsetX + position.X - dragStartPosition.X;
		offsetY = dragStartOffsetY + position.Y - dragStartPosition.Y;
		ClampOffsets(document);
		InvalidateVisual();
	}

	private void MoveElement(Point position)
	{
		if (selectedElement is not { } element || Document is not { } document)
			return;

		element.Position = new SKPoint(
			moveStartPosition.X + (float)((position.X - dragStartPosition.X) / scale),
			moveStartPosition.Y + (float)((position.Y - dragStartPosition.Y) / scale));
		document.RaiseChanged();
	}

	private void BeginScale(Corner corner)
	{
		var element = selectedElement!;
		var bounds = element.Bounds;

		scaleAnchor = corner switch
		{
			Corner.TopLeft => new SKPoint(bounds.Right, bounds.Bottom),
			Corner.TopRight => new SKPoint(bounds.Left, bounds.Bottom),
			Corner.BottomRight => new SKPoint(bounds.Left, bounds.Top),
			_ => new SKPoint(bounds.Right, bounds.Top),
		};

		scaleStart = element.BeginScale();
		dragMode = DragMode.ScaleElement;
		Cursor = GetCornerCursor(corner);
	}

	private void ScaleElement(Point position, PointerEventArgs pointerEvent)
	{
		if (selectedElement is not { } element || Document is not { } document || scaleStart is not { } start)
			return;

		element.ScaleTo(ScreenToDocument(position), scaleAnchor, start, pointerEvent);
		document.RaiseChanged();
	}

	private void UpdateHoverCursor(Point position)
	{
		if (selectedElement is not null && HitTestHandle(position) is { } corner)
		{
			Cursor = GetCornerCursor(corner);
		}
		else if (HitTestElement(position) is not null)
		{
			Cursor = MoveCursor;
		}
		else
		{
			Cursor = Cursor.Default;
		}
	}

	private CanvasElement? HitTestElement(Point position)
	{
		var document = Document;
		if (document is null || previewBitmap is null)
			return null;

		var point = ScreenToDocument(position);
		for (var i = document.Elements.Count - 1; i >= 0; i--)
		{
			if (document.Elements[i].Contains(point))
				return document.Elements[i];
		}

		return null;
	}

	private Corner? HitTestHandle(Point position)
	{
		var document = Document;
		if (selectedElement is null || document is null || previewBitmap is null)
			return null;

		var imageRect = GetImageRect(document);
		var bounds = selectedElement.Bounds;

		foreach (var corner in Corners)
		{
			var screen = ToScreen(GetCorner(bounds, corner), imageRect);
			if (Math.Abs(position.X - screen.X) <= ThemeDefinition.HandleHitRadius &&
				Math.Abs(position.Y - screen.Y) <= ThemeDefinition.HandleHitRadius)
				return corner;
		}

		return null;
	}

	private static SKPoint GetCorner(SKRect bounds, Corner corner) => corner switch
	{
		Corner.TopLeft => new SKPoint(bounds.Left, bounds.Top),
		Corner.TopRight => new SKPoint(bounds.Right, bounds.Top),
		Corner.BottomRight => new SKPoint(bounds.Right, bounds.Bottom),
		_ => new SKPoint(bounds.Left, bounds.Bottom),
	};

	private static Cursor GetCornerCursor(Corner corner) => corner switch
	{
		Corner.TopLeft => TopLeftCursor,
		Corner.TopRight => TopRightCursor,
		Corner.BottomRight => BottomRightCursor,
		_ => BottomLeftCursor,
	};

	private SKPoint ScreenToDocument(Point position)
	{
		var imageRect = GetImageRect(Document!);
		return new SKPoint((float)((position.X - imageRect.X) / scale), (float)((position.Y - imageRect.Y) / scale));
	}

	private Point ToScreen(SKPoint point, Rect imageRect) => new Point(imageRect.X + point.X * scale, imageRect.Y + point.Y * scale);

	private bool ShowRulerContextMenu(Point position)
	{
		var horizontal = position.X >= ThemeDefinition.RulerThickness && position.Y < ThemeDefinition.RulerThickness;
		var vertical = position.X < ThemeDefinition.RulerThickness && position.Y >= ThemeDefinition.RulerThickness;
		if (!horizontal && !vertical)
			return false;

		for (int i = 0; i < rulerContextMenu.Items.Count; i++)
		{
			MenuItem? item = rulerContextMenu.Items[i] as MenuItem;
			if (item == null)
				continue;
			item.IsChecked = currentRulerUnit == (RulerUnit)i;
		}

		OpenMenu(rulerContextMenu);
		return true;
	}

	private bool ShowCanvasContextMenu(Point position)
	{
		if (Document is not { Width: > 0, Height: > 0 } || previewBitmap is null)
			return false;

		contextMenuPosition = ScreenToDocument(position);
		OpenMenu(canvasContextMenu);
		return true;
	}

	private void OpenMenu(ContextMenu menu)
	{
		//A stupid hack because if you right click and call open, the open will run before the previous closing
		//because of some stupid nonsense Avalonia does in background
		//Don't question - it's a small detail, but makes it feel so much nicer
		if (menu.IsOpen)
		{
			menu.Close();
			Dispatcher.UIThread.Post(() => menu.Open(this));
		}
		else
			menu.Open(this);
	}

	private ContextMenu BuildRulerContextMenu()
	{
		var menu = new ContextMenu();
		Dictionary<RulerUnit, MenuItem> rulerMenuItems = new();

		foreach (var unit in Enum.GetValues<RulerUnit>())
		{
			var item = new MenuItem
			{
				Header = unit.ToString(),
				ToggleType = MenuItemToggleType.Radio,
			};
			item.Click += (_, _) =>
			{
				currentRulerUnit = unit;
				InvalidateVisual();
			};
			rulerMenuItems.Add(unit, item);
			menu.Items.Add(item);
		}

		return menu;
	}

	private ContextMenu BuildCanvasContextMenu()
	{
		var menu = new ContextMenu();

		var addLabel = new MenuItem { Header = "Add Label..." };
		addLabel.Click += async (_, _) => await AddLabelAsync(contextMenuPosition);

		var addImage = new MenuItem { Header = "Add Image..." };
		addImage.Click += async (_, _) => await AddImageAsync(contextMenuPosition);

		menu.Items.Add(addLabel);
		menu.Items.Add(addImage);
		return menu;
	}

	private async Task AddLabelAsync(SKPoint position)
	{
		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		if (TopLevel.GetTopLevel(this) is not Window window)
			return;

		var text = await TextPromptDialog.Show(window, "Add Label", "Label text", "Label");
		if (string.IsNullOrWhiteSpace(text))
			return;

		var element = new LabelElement { Text = text.Trim(), Position = position };
		document.Add(element);
		selectedElement = element;
		InvalidateVisual();
	}

	private async Task AddImageAsync(SKPoint position)
	{
		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
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

		document.Add(new ImageElement(bitmap, position));
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		e.Handled = true;

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		var factor = Math.Clamp(Math.Pow(ThemeDefinition.WheelFactor, e.Delta.Y), 1 / 1.5, 1.5);
		var newScale = Math.Clamp(scale * factor, ThemeDefinition.MinZoom, ThemeDefinition.MaxZoom);
		if (newScale == scale)
			return;

		// Keep the document point under the cursor fixed while zooming.
		var cursor = e.GetPosition(this);
		var rect = GetImageRect(document);
		var pixelX = (cursor.X - rect.X) / scale;
		var pixelY = (cursor.Y - rect.Y) / scale;

		scale = newScale;
		SetImagePosition(new Rect(
			cursor.X - pixelX * scale,
			cursor.Y - pixelY * scale,
			document.Width * scale,
			document.Height * scale), document);
		InvalidateVisual();
	}

	public override void Render(DrawingContext context)
	{
		var bounds = new Rect(Bounds.Size);
		context.FillRectangle(ThemeDefinition.Background, bounds);
		DrawRulerBorders(context, bounds);

		var document = Document;
		if (document is null || document.Width <= 0 || document.Height <= 0)
			return;

		EnsurePreviewBitmap(document);

		if (!hasInitialFit && bounds.Width > ThemeDefinition.RulerThickness && bounds.Height > ThemeDefinition.RulerThickness)
		{
			var content = GetContentRect(bounds);
			scale = Math.Min(Math.Min(content.Width / document.Width, content.Height / document.Height), 1);
			offsetX = 0;
			offsetY = 0;
			hasInitialFit = true;
		}

		var imageRect = GetImageRect(document);
		context.DrawImage(previewBitmap!, imageRect);
		DrawSelection(context, imageRect);
		DrawRulers(context, bounds, imageRect, new Size(document.Width, document.Height));
	}

	private void EnsurePreviewBitmap(Document document)
	{
		if (previewBitmap is null || previewBitmap.PixelSize.Width != document.Width || previewBitmap.PixelSize.Height != document.Height)
		{
			previewBitmap?.Dispose();
			previewBitmap = new WriteableBitmap(
				new PixelSize(document.Width, document.Height),
				new Vector(96, 96),
				PixelFormat.Bgra8888,
				AlphaFormat.Premul);
			hasInitialFit = false;
			previewDirty = true;
		}

		if (previewDirty)
		{
			RenderPreview(document);
			previewDirty = false;
		}
	}

	private void RenderPreview(Document document)
	{
		using var framebuffer = previewBitmap!.Lock();
		var info = new SKImageInfo(document.Width, document.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
		DocumentRenderer.Render(document, surface.Canvas);
	}

	private void DrawSelection(DrawingContext context, Rect imageRect)
	{
		if (selectedElement is null || previewBitmap is null)
			return;

		var bounds = selectedElement.Bounds;
		var topLeft = ToScreen(new SKPoint(bounds.Left, bounds.Top), imageRect);
		var bottomRight = ToScreen(new SKPoint(bounds.Right, bounds.Bottom), imageRect);
		context.DrawRectangle(ThemeDefinition.SelectionPen, new Rect(topLeft, bottomRight));

		var half = ThemeDefinition.HandleSize / 2;
		foreach (var corner in Corners)
		{
			var screen = ToScreen(GetCorner(bounds, corner), imageRect);
			var handle = new Rect(screen.X - half, screen.Y - half, ThemeDefinition.HandleSize, ThemeDefinition.HandleSize);
			context.FillRectangle(ThemeDefinition.HandleFill, handle);
			context.DrawRectangle(ThemeDefinition.SelectionPen, handle);
		}
	}

	private static Rect GetContentRect(Rect bounds)
	{
		return new Rect(ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness, Math.Max(0, bounds.Width - ThemeDefinition.RulerThickness),	Math.Max(0, bounds.Height - ThemeDefinition.RulerThickness));
	}

	private Rect GetImageRect(Document document)
	{
		var content = GetContentRect(new Rect(Bounds.Size));
		var width = document.Width * scale;
		var height = document.Height * scale;
		return new Rect(
			content.X + content.Width / 2 + offsetX - width / 2,
			content.Y + content.Height / 2 + offsetY - height / 2,
			width,
			height);
	}

	private void SetImagePosition(Rect rect, Document document)
	{
		var content = GetContentRect(new Rect(Bounds.Size));

		offsetX = rect.X - content.X - content.Width / 2 + document.Width * scale / 2;
		offsetY = rect.Y - content.Y - content.Height / 2 + document.Height * scale / 2;
		ClampOffsets(document);
	}

	private void ClampOffsets(Document document)
	{
		var content = GetContentRect(new Rect(Bounds.Size));
		var width = document.Width * scale;
		var height = document.Height * scale;

		// The document must keep at least MinVisibleEdge of overlap with the content rect
		// on each axis, but its edges are allowed to hang off-screen.
		if (content.Width > 2 * ThemeDefinition.MinVisibleEdge)
		{
			offsetX = Math.Clamp(offsetX, ThemeDefinition.MinVisibleEdge - content.Width / 2 - width / 2, content.Width / 2 - ThemeDefinition.MinVisibleEdge + width / 2);
		}

		if (content.Height > 2 * ThemeDefinition.MinVisibleEdge)
		{
			offsetY = Math.Clamp(offsetY, ThemeDefinition.MinVisibleEdge - content.Height / 2 - height / 2, content.Height / 2 - ThemeDefinition.MinVisibleEdge + height / 2);
		}
	}

	private static void DrawRulerBorders(DrawingContext context, Rect bounds)
	{
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(0, ThemeDefinition.RulerThickness), new Point(bounds.Width, ThemeDefinition.RulerThickness));
		context.DrawLine(ThemeDefinition.RulerBorderPen, new Point(ThemeDefinition.RulerThickness, 0), new Point(ThemeDefinition.RulerThickness, bounds.Height));
	}

	private void DrawRulers(DrawingContext context, Rect bounds, Rect imageRect, Size imageSize)
	{
		var horizontalStrip = new Rect(ThemeDefinition.RulerThickness, 0, bounds.Width - ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness);
		var verticalStrip = new Rect(0, ThemeDefinition.RulerThickness, ThemeDefinition.RulerThickness, bounds.Height - ThemeDefinition.RulerThickness);

		context.FillRectangle(ThemeDefinition.RulerBackground, horizontalStrip);
		context.FillRectangle(ThemeDefinition.RulerBackground, verticalStrip);

		DrawTicks(context, horizontalStrip, imageRect.X, imageSize.Width, currentRulerUnit, horizontal: true);
		DrawTicks(context, verticalStrip, imageRect.Y, imageSize.Height, currentRulerUnit, horizontal: false);
	}

	private void DrawTicks(DrawingContext context, Rect strip, double imageStart, double imageSize, RulerUnit unit, bool horizontal)
	{
		var unitsPerPixel = unit == RulerUnit.Percent ? 100 / imageSize : 1;
		var step = ChooseStep(ThemeDefinition.MinMajorTickSpacing * unitsPerPixel / scale);
		if (unit == RulerUnit.Percent && step > 100)
			step = 100;

		var minorPixels = step / unitsPerPixel / 5;
		var firstTick = (int)Math.Ceiling(Math.Max(0, ((horizontal ? strip.X : strip.Y) - imageStart) / scale) / minorPixels);

		using var clipState = context.PushClip(strip);
		for (var tick = firstTick; ; tick++)
		{
			var pixel = tick * minorPixels;
			if (pixel > imageSize)
				break;

			var position = imageStart + pixel * scale;
			var stripEnd = horizontal ? strip.Right : strip.Bottom;
			if (position > stripEnd)
				break;

			var isMajor = tick % 5 == 0;
			var label = FormatTickLabel(pixel * unitsPerPixel, unit);

			if (horizontal)
			{
				var majorTop = ThemeDefinition.RulerThickness - 9;
				var minorTop = ThemeDefinition.RulerThickness - 5;
				context.DrawLine(ThemeDefinition.TickPen, new Point(position, isMajor ? majorTop : minorTop), new Point(position, ThemeDefinition.RulerThickness - 1));
				if (isMajor)
				{
					context.DrawText(labelCache.Get(label), new Point(position + 3, 2));
				}
			}
			else
			{
				var majorStart = ThemeDefinition.RulerThickness - 9;
				var minorStart = ThemeDefinition.RulerThickness - 5;
				context.DrawLine(ThemeDefinition.TickPen, new Point(isMajor ? majorStart : minorStart, position), new Point(ThemeDefinition.RulerThickness - 1, position));
				if (isMajor)
				{
					var formatted = labelCache.Get(label);
					using var transformState = context.PushTransform(
						Matrix.CreateRotation(-Math.PI / 2) *
						Matrix.CreateTranslation(ThemeDefinition.RulerThickness - 3 - formatted.Height, position + 2 + formatted.Width));
					context.DrawText(formatted, new Point());
				}
			}
		}
	}

	private static string FormatTickLabel(double value, RulerUnit unit)
	{
		if (unit == RulerUnit.Pixels)
			return ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

		return value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
	}

	private static double ChooseStep(double minUnits)
	{
		var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(minUnits, 0.000001))));
		var fraction = minUnits / magnitude;
		var coefficient = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
		return coefficient * magnitude;
	}

	private sealed class FormattedTextCache
	{
		private readonly Dictionary<string, FormattedText> cache = new();

		public FormattedText Get(string text)
		{
			if (!cache.TryGetValue(text, out var formatted))
			{
				formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeDefinition.LabelTypeface, ThemeDefinition.LabelFontSize, ThemeDefinition.RulerLabel);
				cache[text] = formatted;
			}

			return formatted;
		}
	}
}
