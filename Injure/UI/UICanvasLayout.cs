// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Numerics;

namespace Injure.UI;

public static class UICanvasLayout {
	public static UICanvasTransform Compute(UICanvasPolicy policy, SizeI drawableSize) {
		if (drawableSize.Width < 0)
			throw new ArgumentOutOfRangeException(nameof(drawableSize), "drawable width must not be negative");
		if (drawableSize.Height < 0)
			throw new ArgumentOutOfRangeException(nameof(drawableSize), "drawable height must not be negative");

		if (drawableSize.Width == 0 || drawableSize.Height == 0) {
			RectF logicalEmpty = computeLogicalRect(policy, new SizeI(1, 1));
			return new UICanvasTransform(
				LogicalRect: logicalEmpty,
				ViewportRect: RectI.Empty,
				Scale: Vector2.One
			);
		}

		RectF logical = computeLogicalRect(policy, drawableSize);
		RectI viewport = computeViewport(policy, logical, drawableSize);

		Vector2 scale = viewport.Width == 0 || viewport.Height == 0 ? Vector2.One : new Vector2(
			(float)viewport.Width / logical.Width,
			(float)viewport.Height / logical.Height
		);
		return new UICanvasTransform(
			LogicalRect: logical,
			ViewportRect: viewport,
			Scale: scale
		);
	}

	public static Matrix3x2 CreateTransform(UICanvasTransform tx) {
		return
			Matrix3x2.CreateTranslation(-tx.LogicalRect.X, -tx.LogicalRect.Y) *
			Matrix3x2.CreateScale(tx.Scale) *
			Matrix3x2.CreateTranslation(tx.ViewportRect.X, tx.ViewportRect.Y);
	}

	public static Vector2 ScreenToLogical(UICanvasTransform tx, Vector2 targetPos) {
		return new Vector2(
			tx.LogicalRect.X + (targetPos.X - tx.ViewportRect.X) / tx.Scale.X,
			tx.LogicalRect.Y + (targetPos.Y - tx.ViewportRect.Y) / tx.Scale.Y
		);
	}

	public static Vector2 LogicalToScreen(UICanvasTransform tx, Vector2 logicalPos) {
		return new Vector2(
			tx.ViewportRect.X + (logicalPos.X - tx.LogicalRect.X) * tx.Scale.X,
			tx.ViewportRect.Y + (logicalPos.Y - tx.LogicalRect.Y) * tx.Scale.Y
		);
	}

	public static RectI LogicalToScissor(UICanvasTransform tx, RectF logicalRect) {
		Vector2 a = LogicalToScreen(tx, logicalRect.Position);
		Vector2 b = LogicalToScreen(tx, new Vector2(logicalRect.Right, logicalRect.Bottom));

		int left = (int)MathF.Floor(MathF.Min(a.X, b.X));
		int top = (int)MathF.Floor(MathF.Min(a.Y, b.Y));
		int right = (int)MathF.Ceiling(MathF.Max(a.X, b.X));
		int bottom = (int)MathF.Ceiling(MathF.Max(a.Y, b.Y));

		return RectI.FromLTRB(left, top, right, bottom);
	}

	private static RectF computeLogicalRect(UICanvasPolicy policy, SizeI drawableSize) {
		if (policy.Mode == UICanvasMode.MatchDrawable)
			return new RectF(0f, 0f, drawableSize.Width, drawableSize.Height);

		float refW = policy.ReferenceSize.Width;
		float refH = policy.ReferenceSize.Height;
		if (refW <= 0f)
			throw new InvalidOperationException("UI canvas reference width must be positive");
		if (refH <= 0f)
			throw new InvalidOperationException("UI canvas reference height must be positive");

		float logicalW = refW;
		float logicalH = refH;

		switch (policy.Mode.Tag) {
		case UICanvasMode.Case.Fixed:
			break;
		case UICanvasMode.Case.FixedHeightExpandWidth:
			logicalH = refH;
			logicalW = (float)((double)drawableSize.Width * (double)refH / (double)drawableSize.Height);
			break;
		case UICanvasMode.Case.FixedWidthExpandHeight:
			logicalW = refW;
			logicalH = (float)((double)drawableSize.Height * (double)refW / (double)drawableSize.Width);
			break;
		default:
			throw new UnreachableException();
		}

		return new RectF(0f, 0f, logicalW, logicalH);
	}

	private static RectI computeViewport(UICanvasPolicy policy, RectF logicalRect, SizeI drawableSize) {
		if (policy.Mode == UICanvasMode.MatchDrawable)
			return new RectI(0, 0, drawableSize.Width, drawableSize.Height);
		return policy.FitMode.Tag switch {
			UICanvasFitMode.Case.Stretch => new RectI(0, 0, drawableSize.Width, drawableSize.Height),
			UICanvasFitMode.Case.Letterbox => policy.ScaleMode.Tag switch {
				UICanvasScaleMode.Case.Fractional => computeLetterboxedFracScale(logicalRect.Size, drawableSize),
				UICanvasScaleMode.Case.Integer => computeLetterboxedIntScale(logicalRect.Size, drawableSize),
				_ => throw new InternalStateException("unexpected UICanvasScaleMode value"),
			},
			_ => throw new UnreachableException(),
		};
	}

	private static RectI computeLetterboxedFracScale(SizeF logicalSize, SizeI drawableSize) {
		int dw = drawableSize.Width;
		int dh = drawableSize.Height;
		double lw = logicalSize.Width;
		double lh = logicalSize.Height;

		int vpW;
		int vpH;
		if ((double)dw * lh <= (double)dh * lw) {
			// limited by width
			vpW = dw;
			vpH = Math.Clamp((int)Math.Floor((double)dw * lh / lw), 0, dh);
		} else {
			// limited by height
			vpW = Math.Clamp((int)Math.Floor((double)dh * lw / lh), 0, dw);
			vpH = dh;
		}
		return new RectI((dw - vpW) / 2, (dh - vpH) / 2, vpW, vpH);
	}

	private static RectI computeLetterboxedIntScale(SizeF logicalSize, SizeI drawableSize) {
		int dw = drawableSize.Width;
		int dh = drawableSize.Height;
		double lw = logicalSize.Width;
		double lh = logicalSize.Height;

		// largest scale that fits
		int s = (int)Math.Floor(Math.Min((double)dw / lw, (double)dh / lh));

		// nothing fits so we're kind of forced to do a fractional scale
		if (s <= 0)
			return computeLetterboxedFracScale(logicalSize, drawableSize);

		int vpW = Math.Clamp((int)Math.Floor(lw * s), 0, dw);
		int vpH = Math.Clamp((int)Math.Floor(lh * s), 0, dh);
		return new RectI((dw - vpW) / 2, (dh - vpH) / 2, vpW, vpH);
	}
}
