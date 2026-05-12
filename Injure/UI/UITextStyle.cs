// SPDX-License-Identifier: MIT

using System;

using Injure.Graphics.Text;

namespace Injure.UI;

public readonly record struct UITextStyle(
	FontFallbackChain Fonts,
	float Size,
	Color32 Color,
	TextWrapMode WrapMode = default,
	TextHorizontalAlign HorizontalAlign = default,
	string Locale = "und",
	string? LanguageBCP47 = null,
	FontRasterMode RasterMode = default,
	FontHinting Hinting = default,
	bool UseEmbeddedBitmaps = true
);

public static class UITextStyleUtil {
	public static int ResolvePixelSize(UITextStyle style, float textScale) {
		if (!float.IsFinite(textScale) || textScale <= 0f)
			textScale = 1f;
		return Math.Max(1, (int)MathF.Round(style.Size * textScale));
	}

	public static TextStyle ToEngineStyle(UITextStyle style, float textScale, float maxLogicalWidth) {
		int px = ResolvePixelSize(style, textScale);
		float maxPx = float.IsPositiveInfinity(maxLogicalWidth) ? float.PositiveInfinity :
			MathF.Max(0f, maxLogicalWidth * textScale);
		return new TextStyle(
			new FontOptions(px, style.RasterMode, style.Hinting, style.UseEmbeddedBitmaps),
			style.Color,
			new TextLayoutOptions(maxPx, style.WrapMode, style.HorizontalAlign),
			style.Locale,
			style.LanguageBCP47
		);
	}
}

public sealed class UITheme {
	public required UITextStyle BodyText { get; init; }
	public required UITextStyle ButtonText { get; init; }
	public required UITextStyle HeadingText { get; init; }
}
