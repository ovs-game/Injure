// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Injure.Graphics.Text;

public sealed class LiveText : IDisposable {
	private readonly record struct Request(FontFallbackChain Fonts, string Text, TextStyle Style);

	private readonly TextSystem owner;
	private Request req;
	private TextLayout layout;
	private ulong chainHash;
	private bool disposed = false;

	public FontFallbackChain Fonts { get { ObjectDisposedException.ThrowIf(disposed, this); return req.Fonts; } }
	public string Text { get { ObjectDisposedException.ThrowIf(disposed, this); return req.Text; } }
	public TextStyle Style { get { ObjectDisposedException.ThrowIf(disposed, this); return req.Style; } }
	public TextLayout Layout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
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
		ObjectDisposedException.ThrowIf(disposed, this);
		refreshIfNeeded();
		layout.Render(cv, at);
	}

	public void SetText(ReadOnlySpan<char> text) {
		ObjectDisposedException.ThrowIf(disposed, this);
		string s = new(text);
		if (req.Text == s)
			return;
		req = req with { Text = s };
		rebuild();
	}

	public void SetParams(FontFallbackChain? fonts = null, string? text = null, TextStyle? style = null) {
		ObjectDisposedException.ThrowIf(disposed, this);
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
		layout?.Dispose();
		layout = owner.Layout(req.Fonts, req.Text, req.Style);
		chainHash = h;
	}

	[MemberNotNull(nameof(layout))]
	private void rebuild() {
		layout?.Dispose();
		layout = owner.Layout(req.Fonts, req.Text, req.Style);
		chainHash = req.Fonts.Hash();
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		layout.Dispose();
	}
}
