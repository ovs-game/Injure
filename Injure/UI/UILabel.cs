// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Injure.Graphics;
using Injure.Graphics.Text;

namespace Injure.UI;

public sealed class UILabel : UIWidget {
	private readonly TextSystem textSystem;
	private string value = string.Empty;
	private LiveText? live;
	private string? liveTextValue;
	private TextStyle liveStyle;

	public string Text {
		get => value;
		set => this.value = value ?? string.Empty;
	}

	public UITextStyle Style { get; set; }

	public UILabel(TextSystem textSystem, UITextStyle style, string text) {
		this.textSystem = textSystem ?? throw new ArgumentNullException(nameof(textSystem));
		Style = style;
		Text = text;
	}

	protected override SizeF MeasureCore(in UILayoutContext ctx, in UISizeConstraint constraint) {
		float maxLogicalWidth = constraint.MaxWidth;
		TextStyle engineStyle = UITextStyleUtil.ToEngineStyle(Style, ctx.TextScale, maxLogicalWidth);
		TextMeasurement m = textSystem.Measure(Style.Fonts, Text, in engineStyle);

		return constraint.Clamp(new SizeF(
			m.Width / ctx.TextScale + Padding.Horizontal,
			m.Height / ctx.TextScale + Padding.Vertical
		));
	}

	public override void Render(Canvas cv, in UIRenderContext ctx) {
		if (!Visible)
			return;

		RectF inner = LayoutRect.Deflate(Padding);
		TextStyle engineStyle = UITextStyleUtil.ToEngineStyle(Style, ctx.TextScale, inner.Width);
		ensure(engineStyle);
		Vector2 targetAt = ctx.LogicalToTarget(inner.Position);
		using (cv.PushParams(new CanvasParamsOverride(Transform: Matrix3x2.Identity))) {
			live.Render(cv, targetAt);
		}
	}

	[MemberNotNull(nameof(live))]
	private void ensure(TextStyle engineStyle) {
		if (live is null) {
			live = textSystem.Make(Style.Fonts, Text, engineStyle);
			liveTextValue = Text;
			liveStyle = engineStyle;
			return;
		}
		if (!StringComparer.Ordinal.Equals(liveTextValue, Text) || !liveStyle.Equals(engineStyle)) {
			live.SetParams(Style.Fonts, Text, engineStyle);
			liveTextValue = Text;
			liveStyle = engineStyle;
		}
	}
}
