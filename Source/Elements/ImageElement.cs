using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace WhatSchedule.Elements
{
	public sealed class ImageElement : CanvasElement
	{
		private static readonly SKPaint BitmapPaint = new()
		{
			IsAntialias = true,		
		};

		public SKBitmap Bitmap { get; }

		public float Width { get; set; }

		public float Height { get; set; }

		public ImageElement(SKBitmap bitmap, SKPoint position)
		{
			Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
			Position = position;
			Width = bitmap.Width;
			Height = bitmap.Height;
		}

		public override SKSize Size => new(Width, Height);

		public override ScaleState BeginScale() => new ImageScaleState(Width, Height);

		public override void ScaleTo(SKPoint cursor, SKPoint anchor, ScaleState state, PointerEventArgs pointerEvent)
		{
			if (state is not ImageScaleState start)
				return;

			if (pointerEvent.KeyModifiers.HasFlag(KeyModifiers.Shift))
			{
				//Preserve aspect ratio
				var minFactor = Math.Max(ThemeDefinition.MinElementSize / Math.Max(start.Width, 1), ThemeDefinition.MinElementSize / Math.Max(start.Height, 1));
				var factor = Math.Max(Math.Max(Math.Abs(cursor.X - anchor.X) / Math.Max(start.Width, 1), Math.Abs(cursor.Y - anchor.Y) / Math.Max(start.Height, 1)), minFactor);

				var width = (float)(start.Width * factor);
				var height = (float)(start.Height * factor);
				Width = width;
				Height = height;
				Position = new SKPoint(cursor.X < anchor.X ? anchor.X - width : anchor.X, cursor.Y < anchor.Y ? anchor.Y - height : anchor.Y);
			}
			else
			{
				Width = (float)Math.Max(ThemeDefinition.MinElementSize, Math.Abs(cursor.X - anchor.X));
				Height = (float)Math.Max(ThemeDefinition.MinElementSize, Math.Abs(cursor.Y - anchor.Y));
				Position = new SKPoint(Math.Min(anchor.X, cursor.X), Math.Min(anchor.Y, cursor.Y));
			}
		}

		public override void Draw(SKCanvas canvas) => canvas.DrawBitmap(Bitmap, SKRect.Create(Position, Size), BitmapPaint);

		public override ElementState CaptureState() => new ImageElementState(Position, Width, Height);

		public override void RestoreState(ElementState state)
		{
			if (state is not ImageElementState snapshot)
				return;

			Position = snapshot.Position;
			Width = snapshot.Width;
			Height = snapshot.Height;
		}

		public override void BuildInspectorRows(IInspectorBuilder builder)
		{
			builder.AddSection("Size");
			builder.AddNumber("Width", () => Width, value => Width = (float)Math.Max(ThemeDefinition.MinElementSize, value));
			builder.AddNumber("Height", () => Height, value => Height = (float)Math.Max(ThemeDefinition.MinElementSize, value));

			var ratio = new TextBlock { Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
			void RefreshRatio()
			{
				var current = Height != 0 ? Width / Height : 0;
				var original = (float)Bitmap.Width / Bitmap.Height;
				ratio.Text = $"{current:0.###} (original {original:0.###})";
			}

			RefreshRatio();
			builder.AddRow("Aspect Ratio", ratio);
			builder.AddSync(RefreshRatio);

			var reset = new Button { Content = "Reset Aspect Ratio", HorizontalAlignment = HorizontalAlignment.Stretch };
			reset.Click += (_, _) => builder.Apply(() =>
			{
				var original = (float)Bitmap.Width / Bitmap.Height;
				Height = (float)Math.Max(ThemeDefinition.MinElementSize, Width / original);
			});
			builder.AddRow(string.Empty, reset);
		}

		private sealed record ImageScaleState(float Width, float Height) : ScaleState;

		public sealed record ImageElementState(SKPoint Position, float Width, float Height) : ElementState;
	}
}
