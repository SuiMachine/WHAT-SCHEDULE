using Avalonia.Input;
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

		private sealed record ImageScaleState(float Width, float Height) : ScaleState;

		public sealed record ImageElementState(SKPoint Position, float Width, float Height) : ElementState;
	}
}
