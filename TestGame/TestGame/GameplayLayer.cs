// SPDX-License-Identifier: MIT

using System;
using System.Numerics;
using Injure;
using Injure.Graphics;
using Injure.Graphics.Text;
using Injure.Input;
using Injure.Layers;
using Injure.UI;

namespace TestGame;

public sealed class GameplayLayer : Layer {
	private readonly LayerTagSet tags = new([LayerTags.Gameplay]);

	private const float speed = 220f;
	private const float flashDecay = 2.5f;
	private Vector2 pos = new(200, 200);
	private float size = 40f;
	private float flash = 0f;
	private UIRoot? ui;

	public override LayerFeatures Features => LayerFeatures.Render | LayerFeatures.Input;
	public override LayerTagSet Tags => tags;
	public override ActionProfile? ActionProfile => Actions.Profile;

	public override void OnEnter() {
		UITextStyle style = new(
			Fonts: new FontFallbackChain(Game.TestFont),
			Size: 32f,
			Color: new Color32(128, 244, 216)
		);
		UILabel label = new(Game.Text, style, "ficelle\u0301 fffffi AVAVAV. ToToTo WaWaWa");
		UIDebugRect rect = new(Color32.Blue) {
			Stroke = Color32.Magenta,
			StrokeWidth = 4f,
		};
		UIOverlay root = new();
		root.Add(new UIPlaced(label, UIPlacement.CenterAuto()));
		root.Add(new UIPlaced(rect, UIPlacement.AnchorAt(UIAnchor.TopRight, offset: new Vector2(-32f, 32f), size: new SizeF(200f, 50f))));
		ui = new(UICanvasPolicy.MatchDrawable) { // note: normally you'll wanna use Fixed or something like that, this is for debugging
			RootWidget = root,
		};
	}

	public override void Update(in LayerTickContext ctx) {
		if (ctx.Actions.Buttons[Actions.Pause].Pressed)
			Game.Mods.RequestReload("jdoe.test-mod", Injure.ModKit.Runtime.ReloadRequestKind.SafeBoundary);
		/*
		if (ctx.Actions.Buttons[Actions.Pause].Pressed)
			throw new NotImplementedException();
		*/
		if (ui is null)
			throw new InvalidOperationException("Update() called before OnEnter()");
		ui.Update(ctx.Controls, Game.WindowState);

		Vector2 move = ctx.Actions.StateAxes2D[Actions.Move].Value;
		pos += move * speed * (float)ctx.DeltaTime;

		if (ctx.Actions.Buttons[Actions.Confirm].Pressed)
			flash = 1f;
		flash = MathF.Max(0f, flash - flashDecay * (float)ctx.DeltaTime);

		float scroll = ctx.Actions.ImpulseAxes[Actions.ScrollY].Amount;
		if (scroll != 0f)
			size = Math.Clamp(size + scroll * 8f, 8f, 160f);
	}

	public override void Render(Canvas cv) {
		(ui ?? throw new InvalidOperationException("Render() called before OnEnter()")).Render(cv);
		cv.Rect(new RectF(pos.X, pos.Y, size, size), new Color32((byte)(255 * (1f - flash)), 255, 255));
		cv.Rect(new RectF(20f, 300f, 50f, 50f), GetSomeColor());
	}

	public override void OnLeave() {
	}

	public static Color32 GetSomeColor() {
		return Color32.Magenta;
	}
}
