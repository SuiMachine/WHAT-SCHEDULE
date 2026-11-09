using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace WhatSchedule.Elements
{
	/// <summary>A side of the selection box, for crop drags.</summary>
	internal enum Edge
	{
		Left,
		Top,
		Right,
		Bottom,
	}

	/// <summary>How a bitmap's aspect ratio relates to an image element's box, i.e. which axis is center-cropped when the bitmap is swapped in to fill the box.</summary>
	internal enum BitmapFit
	{
		/// <summary>The bitmap's aspect ratio matches the box; it swaps in uncropped.</summary>
		Same,

		/// <summary>The bitmap is relatively taller than the box; its top and bottom are cropped away.</summary>
		Taller,

		/// <summary>The bitmap is relatively wider than the box; its left and right sides are cropped away.</summary>
		Wider,
	}

	public sealed class ImageElement : CanvasElement
	{
		private static readonly SKPaint BitmapPaint = new()
		{
			IsAntialias = true,
		};

		/// <summary>The blend modes offered for tinting; a curated subset of SKBlendMode rather than all of them.</summary>
		private static readonly SKBlendMode[] TintModes =
		{
			SKBlendMode.SrcIn,
			SKBlendMode.Multiply,
			SKBlendMode.Screen,
			SKBlendMode.Overlay,
			SKBlendMode.SoftLight,
			SKBlendMode.HardLight,
			SKBlendMode.Plus,
		};

		/// <summary>The blend modes offered for compositing the image onto the canvas; a curated subset of SKBlendMode rather than all of them.</summary>
		private static readonly string[] BlendModes =
		{
			SKBlendMode.SrcOver.ToString(), SKBlendMode.Multiply.ToString(), SKBlendMode.Screen.ToString(), SKBlendMode.Overlay.ToString(),
			SKBlendMode.Darken.ToString(), SKBlendMode.Lighten.ToString(), SKBlendMode.HardLight.ToString(), SKBlendMode.SoftLight.ToString(),
			SKBlendMode.Difference.ToString(), SKBlendMode.Exclusion.ToString(), SKBlendMode.Hue.ToString(), SKBlendMode.Saturation.ToString(),
			SKBlendMode.Color.ToString(), SKBlendMode.Luminosity.ToString(), SKBlendMode.Plus.ToString(),
		};

		public SKBitmap Bitmap { get; }

		/// <summary>The element's kind as shown to the user, e.g. in the undo history.</summary>
		public override string Kind => ImageElementData.KindName;

		public float Width { get; set; }

		public float Height { get; set; }

		private SKRect crop;

		/// <summary>The part of the bitmap that is drawn, in bitmap pixels; always kept inside the bitmap and non-empty.</summary>
		public SKRect Crop
		{
			get => crop;
			set => crop = ClampCrop(value);
		}

		/// <summary>How opaque the image is drawn, 0 (invisible) to 1 (fully opaque).</summary>
		public float Opacity { get; set; } = 1f;

		/// <summary>The radius the image's corners are rounded with, in document pixels.</summary>
		public float CornerRadius { get; set; }

		/// <summary>Whether the image is mirrored horizontally.</summary>
		public bool FlipX { get; set; }

		/// <summary>Whether the image is mirrored vertically.</summary>
		public bool FlipY { get; set; }

		/// <summary>The color of the drop shadow; alpha 0 disables the shadow.</summary>
		public SKColor ShadowColor { get; set; }

		/// <summary>How blurred the drop shadow is, in sigma pixels.</summary>
		public float ShadowBlur { get; set; } = 6f;

		/// <summary>How far the drop shadow is displaced, in document pixels.</summary>
		public SKPoint ShadowOffset { get; set; } = new(3f, 3f);

		/// <summary>The color the image is tinted with; alpha 0 disables the tint.</summary>
		public SKColor TintColor { get; set; }

		/// <summary>How the tint color is combined with the image.</summary>
		public SKBlendMode TintMode { get; set; } = SKBlendMode.SrcIn;

		/// <summary>How the image is composited onto the content below it on the canvas.</summary>
		public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

		public ImageElement(SKBitmap bitmap, SKPoint position)
		{
			Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
			Position = position;
			Width = bitmap.Width;
			Height = bitmap.Height;
			Crop = new SKRect(0, 0, bitmap.Width, bitmap.Height);
		}

		public override SKSize Size => new(Width, Height);

		public override ScaleState BeginScale() => new ImageScaleState(Width, Height);

		/// <summary>How many document pixels one bitmap pixel is currently drawn as, horizontally.</summary>
		private float ScaleX => Crop.Width != 0 ? Width / Crop.Width : 1f;

		/// <summary>How many document pixels one bitmap pixel is currently drawn as, vertically.</summary>
		private float ScaleY => Crop.Height != 0 ? Height / Crop.Height : 1f;

		/// <summary>The whole bitmap mapped to document pixels at the current drawn scale, sharing its crop-side edges with the frame; the frame reveals the <see cref="Crop"/> part of it.</summary>
		internal SKRect BitmapExtent => SKRect.Create(
			Bounds.Left - Crop.Left * ScaleX,
			Bounds.Top - Crop.Top * ScaleY,
			Bitmap.Width * ScaleX,
			Bitmap.Height * ScaleY);

		/// <summary>State of an image element captured when a crop drag starts, so every drag step is applied relative to the frame as it was when the drag began.</summary>
		internal sealed record ImageCropState(SKRect Frame, SKRect Crop);

		/// <summary>Captures the frame and crop for <see cref="CropTo"/> during a crop drag.</summary>
		internal ImageCropState BeginCrop() => new(Bounds, Crop);

		/// <summary>Moves the crop's <paramref name="edge"/> so it follows <paramref name="cursor"/> in the element's unrotated local space: dragging an edge inward crops more of the image away, dragging it outward reveals pixels the crop was hiding, and the opposite edge stays fixed. The frame is rebuilt at the drawn scale so the remaining pixels are never stretched.</summary>
		internal void CropTo(SKPoint cursor, Edge edge, ImageCropState state)
		{
			var frame = state.Frame;
			var crop = state.Crop;
			var scaleX = crop.Width != 0 ? state.Frame.Width / crop.Width : 1f;
			var scaleY = crop.Height != 0 ? state.Frame.Height / crop.Height : 1f;

			//The dragged edge is where the cursor is on that axis, converted back to bitmap pixels at the drag's start scale.
			var left = crop.Left;
			var top = crop.Top;
			var right = crop.Right;
			var bottom = crop.Bottom;
			switch (edge)
			{
				case Edge.Left: left = crop.Left + (cursor.X - frame.Left) / scaleX; break;
				case Edge.Top: top = crop.Top + (cursor.Y - frame.Top) / scaleY; break;
				case Edge.Right: right = crop.Right + (cursor.X - frame.Right) / scaleX; break;
				case Edge.Bottom: bottom = crop.Bottom + (cursor.Y - frame.Bottom) / scaleY; break;
			}

			var newCrop = ClampCrop(SKRect.Create(left, top, right - left, bottom - top));

			//The frame keeps the opposite edge where it was, so it only moves or shrinks on the dragged axis.
			var frameLeft = frame.Left;
			var frameTop = frame.Top;
			var frameWidth = frame.Width;
			var frameHeight = frame.Height;
			switch (edge)
			{
				case Edge.Left:
					frameLeft = frame.Left + (newCrop.Left - crop.Left) * scaleX;
					frameWidth = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Width * scaleX);
					break;
				case Edge.Top:
					frameTop = frame.Top + (newCrop.Top - crop.Top) * scaleY;
					frameHeight = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Height * scaleY);
					break;
				case Edge.Right:
					frameWidth = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Width * scaleX);
					break;
				case Edge.Bottom:
					frameHeight = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Height * scaleY);
					break;
			}

			Crop = newCrop;
			Width = frameWidth;
			Height = frameHeight;
			Position = new SKPoint(frameLeft, frameTop);
		}

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

		public override void Draw(SKCanvas canvas, ScheduleActivity? filter)
		{
			var bounds = Bounds;
			var count = canvas.Save();
			canvas.RotateDegrees(Rotation, bounds.MidX, bounds.MidY);
			canvas.Translate(bounds.MidX, bounds.MidY);
			canvas.Scale(FlipX ? -1 : 1, FlipY ? -1 : 1);
			canvas.Translate(-bounds.MidX, -bounds.MidY);

			if (ShadowColor.Alpha > 0)
			{
				//The shadow is cast from the element's own (rounded) shape rather than the
				//bitmap's alpha, so partly transparent PNGs still get a clean card shadow.
				var shadowColor = ShadowColor.WithAlpha((byte)Math.Round(ShadowColor.Alpha * Math.Clamp(Opacity, 0f, 1f)));
				using var shadowFilter = SKImageFilter.CreateDropShadowOnly(ShadowOffset.X, ShadowOffset.Y, ShadowBlur, ShadowBlur, shadowColor);
				BitmapPaint.BlendMode = SKBlendMode.SrcOver;
				BitmapPaint.Color = SKColors.Black;
				BitmapPaint.ImageFilter = shadowFilter;
				if (CornerRadius > 0)
				{
					using var shape = new SKPath();
					shape.AddRoundRect(bounds, CornerRadius, CornerRadius);
					canvas.DrawPath(shape, BitmapPaint);
				}
				else
				{
					canvas.DrawRect(bounds, BitmapPaint);
				}
				BitmapPaint.ImageFilter = null;
			}

			if (CornerRadius > 0)
			{
				canvas.Save();
				using var clip = new SKPath();
				clip.AddRoundRect(bounds, CornerRadius, CornerRadius);
				canvas.ClipPath(clip, SKClipOperation.Intersect, true);
			}

			BitmapPaint.BlendMode = BlendMode;
			BitmapPaint.Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(Opacity, 0f, 1f) * 255));
			canvas.DrawBitmap(Bitmap, Crop, bounds, BitmapPaint);

			if (TintColor.Alpha > 0)
			{
				BitmapPaint.BlendMode = TintMode;
				BitmapPaint.Color = TintColor;
				canvas.DrawBitmap(Bitmap, Crop, bounds, BitmapPaint);
				BitmapPaint.BlendMode = SKBlendMode.SrcOver;
			}

			if (CornerRadius > 0)
				canvas.Restore();

			canvas.RestoreToCount(count);
		}

		/// <summary>Tolerance within which a bitmap's aspect ratio still counts as matching the box, so a near-match swaps in uncropped instead of cropping fractions of a pixel.</summary>
		private const float SameAspectTolerance = 0.005f;

		/// <summary>How the whole <paramref name="bitmap"/> relates to this element's box: matching, or taller or wider by aspect ratio, i.e. which axis would be center-cropped to make the bitmap fill the box.</summary>
		internal BitmapFit MeasureBitmapFit(SKBitmap bitmap)
		{
			if (Width <= 0 || Height <= 0)
				return BitmapFit.Same;

			var boxAspect = Width / Height;
			var bitmapAspect = bitmap.Height > 0 ? (float)bitmap.Width / bitmap.Height : 1f;
			if (Math.Abs(bitmapAspect / boxAspect - 1f) <= SameAspectTolerance)
				return BitmapFit.Same;

			return bitmapAspect < boxAspect ? BitmapFit.Taller : BitmapFit.Wider;
		}

		/// <summary>The part of <paramref name="bitmap"/> that fills this element's box at its aspect ratio, center-cropping the bitmap's surplus axis; the whole bitmap when the aspect ratios match.</summary>
		private SKRect FitBoxCrop(SKBitmap bitmap)
		{
			var fit = MeasureBitmapFit(bitmap);
			if (fit == BitmapFit.Same)
				return new SKRect(0, 0, bitmap.Width, bitmap.Height);

			var boxAspect = Width / Height;
			if (fit == BitmapFit.Taller)
			{
				//The bitmap is relatively taller than the box, so the full width stays and the surplus comes off the top and bottom.
				var height = (float)bitmap.Width / boxAspect;
				return SKRect.Create(0, (bitmap.Height - height) / 2f, bitmap.Width, height);
			}

			//The bitmap is relatively wider than the box, so the full height stays and the surplus comes off the left and right.
			var width = bitmap.Height * boxAspect;
			return SKRect.Create((bitmap.Width - width) / 2f, 0, width, bitmap.Height);
		}

		/// <summary>Creates a replacement element drawing <paramref name="bitmap"/> in this element's exact place: the box, rotation and styling carry over unchanged, and the bitmap fills the box, center-cropping its surplus axis when its aspect ratio does not match the box.</summary>
		internal ImageElement CreateReplacement(SKBitmap bitmap)
		{
			var replacement = new ImageElement(bitmap, Position)
			{
				Rotation = Rotation,
				Width = Width,
				Height = Height,
				Opacity = Opacity,
				CornerRadius = CornerRadius,
				FlipX = FlipX,
				FlipY = FlipY,
				ShadowColor = ShadowColor,
				ShadowBlur = ShadowBlur,
				ShadowOffset = ShadowOffset,
				TintColor = TintColor,
				TintMode = TintMode,
				BlendMode = BlendMode,
				Name = Name,
			};
			replacement.Tag = Tag;
			replacement.Crop = FitBoxCrop(bitmap);
			return replacement;
		}

		/// <summary>Creates a copy of the image element sharing the bitmap (which is never modified) and restoring the rest of its state.</summary>
		public override CanvasElement Clone()
		{
			var clone = new ImageElement(Bitmap, SKPoint.Empty);
			clone.RestoreState(CaptureState());
			clone.Tag = Tag;
			clone.IsLocked = IsLocked;
			clone.IsHidden = IsHidden;
			return clone;
		}

		/// <summary>Clamps <paramref name="crop"/> so it stays inside the bitmap and never collapses to nothing.</summary>
		private SKRect ClampCrop(SKRect crop)
		{
			var left = Math.Clamp(crop.Left, 0, Math.Max(0, Bitmap.Width - 1));
			var top = Math.Clamp(crop.Top, 0, Math.Max(0, Bitmap.Height - 1));
			var right = Math.Clamp(crop.Right, left + 1, Bitmap.Width);
			var bottom = Math.Clamp(crop.Bottom, top + 1, Bitmap.Height);
			return new SKRect(left, top, right, bottom);
		}

		/// <summary>Scales the frame and its attached lengths; the crop stays in bitmap pixels, so the cropped picture scales with the frame.</summary>
		internal override void BakeScale(SKPoint factor)
		{
			Width *= factor.X;
			Height *= factor.Y;
			CornerRadius *= factor.X;
			ShadowBlur *= factor.X;
			ShadowOffset = new SKPoint(ShadowOffset.X * factor.X, ShadowOffset.Y * factor.Y);
		}

		public override ElementState CaptureState() => new ImageElementState(Position, Rotation, Width, Height, Crop, Opacity, CornerRadius, FlipX, FlipY, ShadowColor, ShadowBlur, ShadowOffset, TintColor, TintMode, BlendMode);

		public override void RestoreState(ElementState state)
		{
			if (state is not ImageElementState snapshot)
				return;

			Position = snapshot.Position;
			Rotation = snapshot.Rotation;
			Width = snapshot.Width;
			Height = snapshot.Height;
			Crop = snapshot.Crop;
			Opacity = snapshot.Opacity;
			CornerRadius = snapshot.CornerRadius;
			FlipX = snapshot.FlipX;
			FlipY = snapshot.FlipY;
			ShadowColor = snapshot.ShadowColor;
			ShadowBlur = snapshot.ShadowBlur;
			ShadowOffset = snapshot.ShadowOffset;
			TintColor = snapshot.TintColor;
			TintMode = snapshot.TintMode;
			BlendMode = snapshot.BlendMode;
		}

		public override void BuildInspectorRows(IInspectorBuilder builder)
		{
			builder.AddSection("Image");
			builder.AddImageReplaceButton();

			builder.AddSection("Size");
			builder.AddNumber("Width", () => Width, value => Width = (float)Math.Max(ThemeDefinition.MinElementSize, value));
			builder.AddNumber("Height", () => Height, value => Height = (float)Math.Max(ThemeDefinition.MinElementSize, value));

			var ratio = new TextBlock { Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
			void RefreshRatio()
			{
				var current = Height != 0 ? Width / Height : 0;
				var original = Crop.Height != 0 ? Crop.Width / Crop.Height : 0;
				ratio.Text = $"{current:0.###} (original {original:0.###})";
			}

			RefreshRatio();
			builder.AddRow("Aspect Ratio", ratio);
			builder.AddSync(RefreshRatio);

			var reset = new Button { Content = "Reset Aspect Ratio", HorizontalAlignment = HorizontalAlignment.Stretch };
			reset.Click += (_, _) => builder.Apply("Reset Aspect Ratio", () =>
			{
				var original = Crop.Height != 0 ? Crop.Width / Crop.Height : 0;
				Height = (float)Math.Max(ThemeDefinition.MinElementSize, Width / original);
			});
			builder.AddRow(string.Empty, reset);

			builder.AddSection("Crop");
			//Each number is how many bitmap pixels are trimmed off that edge; the opposite edge stays fixed, so editing one side can only ever cut that side.
			//Applying a crop also resizes the element to the new crop at the drawn scale and shifts it by the edges that moved,
			//so the remaining pixels stay put and are never stretched to refill the old frame.
			void ApplyCrop(SKRect newCrop)
			{
				var scaleX = Crop.Width != 0 ? Width / Crop.Width : 1f;
				var scaleY = Crop.Height != 0 ? Height / Crop.Height : 1f;
				Position = new SKPoint(Position.X + (newCrop.Left - Crop.Left) * scaleX, Position.Y + (newCrop.Top - Crop.Top) * scaleY);
				Crop = newCrop;
				Width = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Width * scaleX);
				Height = (float)Math.Max(ThemeDefinition.MinElementSize, newCrop.Height * scaleY);
			}

			builder.AddNumber("Left", () => Crop.Left, value =>
			{
				var left = Math.Clamp(value, 0f, Crop.Right - 1f);
				ApplyCrop(SKRect.Create(left, Crop.Top, Crop.Right - left, Crop.Height));
			}, 0f);
			builder.AddNumber("Right", () => Bitmap.Width - Crop.Right, value =>
			{
				var right = Math.Clamp(Bitmap.Width - value, Crop.Left + 1f, Bitmap.Width);
				ApplyCrop(SKRect.Create(Crop.Left, Crop.Top, right - Crop.Left, Crop.Height));
			}, 0f);
			builder.AddNumber("Top", () => Crop.Top, value =>
			{
				var top = Math.Clamp(value, 0f, Crop.Bottom - 1f);
				ApplyCrop(SKRect.Create(Crop.Left, top, Crop.Width, Crop.Bottom - top));
			}, 0f);
			builder.AddNumber("Bottom", () => Bitmap.Height - Crop.Bottom, value =>
			{
				var bottom = Math.Clamp(Bitmap.Height - value, Crop.Top + 1f, Bitmap.Height);
				ApplyCrop(SKRect.Create(Crop.Left, Crop.Top, Crop.Width, bottom - Crop.Top));
			}, 0f);

			var resetCrop = new Button { Content = "Reset Crop", HorizontalAlignment = HorizontalAlignment.Stretch };
			resetCrop.Click += (_, _) => builder.Apply("Reset Crop", () => ApplyCrop(new SKRect(0, 0, Bitmap.Width, Bitmap.Height)));
			builder.AddRow(string.Empty, resetCrop);

			builder.AddSection("Appearance");
			builder.AddComboBox("Blend Mode", BlendModes, () => BlendMode.ToString(), value => BlendMode = Enum.Parse<SKBlendMode>(value));
			builder.AddSlider("Opacity %", 0, 100, () => Opacity * 100f, value => Opacity = (float)Math.Clamp(value, 0, 100) / 100f);
			builder.AddNumber("Corner Radius", () => CornerRadius, value => CornerRadius = Math.Max(0, value));
			builder.AddToggle("Flip Horizontal", () => FlipX, value => FlipX = value);
			builder.AddToggle("Flip Vertical", () => FlipY, value => FlipY = value);

			builder.AddSection("Drop Shadow");
			builder.AddColor("Color", () => ShadowColor, value => ShadowColor = value);
			builder.AddNumber("Blur", () => ShadowBlur, value => ShadowBlur = Math.Max(0, value));
			builder.AddNumber("Offset X", () => ShadowOffset.X, value => ShadowOffset = new SKPoint(value, ShadowOffset.Y));
			builder.AddNumber("Offset Y", () => ShadowOffset.Y, value => ShadowOffset = new SKPoint(ShadowOffset.X, value));

			builder.AddSection("Tint");
			builder.AddColor("Color", () => TintColor, value => TintColor = value);

			builder.AddComboBox("Mode", TintModes.Select(mode => mode.ToString()).ToArray(), () => TintMode.ToString(), value => TintMode = Enum.Parse<SKBlendMode>(value));
		}

		private sealed record ImageScaleState(float Width, float Height) : ScaleState;

		public sealed record ImageElementState(SKPoint Position, float Rotation, float Width, float Height, SKRect Crop, float Opacity, float CornerRadius, bool FlipX, bool FlipY, SKColor ShadowColor, float ShadowBlur, SKPoint ShadowOffset, SKColor TintColor, SKBlendMode TintMode, SKBlendMode BlendMode) : ElementState;

		public sealed class ImageElementData : ElementData
		{
			public const string KindName = "Image";

			public override string Kind => KindName;

			public string? Png { get; set; }

			public float? Width { get; set; }

			public float? Height { get; set; }

			/// <summary>Pixels trimmed from the bitmap's left edge; 0 keeps the edge.</summary>
			public float CropLeft { get; set; }

			/// <summary>Pixels trimmed from the bitmap's top edge; 0 keeps the edge.</summary>
			public float CropTop { get; set; }

			/// <summary>Pixels trimmed from the bitmap's right edge; 0 keeps the edge.</summary>
			public float CropRight { get; set; }

			/// <summary>Pixels trimmed from the bitmap's bottom edge; 0 keeps the edge.</summary>
			public float CropBottom { get; set; }

			public float? Opacity { get; set; }

			public float? CornerRadius { get; set; }

			public bool? FlipX { get; set; }

			public bool? FlipY { get; set; }

			public string? ShadowColor { get; set; }

			public float? ShadowBlur { get; set; }

			public CanvasElement.PointData? ShadowOffset { get; set; }

			public string? TintColor { get; set; }

			public string? TintMode { get; set; }

			/// <summary>How the image is composited onto the canvas; null in documents saved before image blend modes existed.</summary>
			public string? BlendMode { get; set; }
		}
	}
}
