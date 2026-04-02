// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Injure.Graphics.Text;

public sealed class LiveText {
	private readonly record struct Request(FontFallbackChain Fonts, string Text, TextStyle Style);

	private readonly TextSystem owner;
	private Request req;
	private TextLayout layout;
	private ulong chainHash;

	public FontFallbackChain Fonts => req.Fonts;
	public string Text => req.Text;
	public TextStyle Style => req.Style;
	public TextLayout Layout {
		get {
			refreshIfNeeded();
			return layout;
		}
	}

	internal LiveText(TextSystem owner, FontFallbackChain fonts, ReadOnlySpan<char> text, TextStyle style) {
		this.owner = owner;
		req = new Request(fonts, new string(text), style);
		rebuild();
	}

	public void Render(Canvas cv, Vector2 at = default) {
		refreshIfNeeded();
		layout.Render(cv, at);
	}

	public void SetText(ReadOnlySpan<char> text) {
		string s = new string(text);
		if (req.Text == s)
			return;
		req = req with { Text = s };
		rebuild();
	}

	public void SetParams(FontFallbackChain? fonts = null, string? text = null, TextStyle? style = null) {
		if (fonts is null && text is null && style is null)
			return;
		req = req with {
			Fonts = fonts ?? req.Fonts,
			Text = text ?? req.Text,
			Style = style ?? req.Style
		};
		rebuild();
	}

	private void refreshIfNeeded() {
		ulong h = req.Fonts.Hash();
		if (h == chainHash)
			return;
		layout = owner.Layout(req.Fonts, req.Text, req.Style);
		chainHash = h;
	}

	[MemberNotNull(nameof(layout))]
	private void rebuild() {
		layout = owner.Layout(req.Fonts, req.Text, req.Style);
		chainHash = req.Fonts.Hash();
	}
}
