// SPDX-License-Identifier: MIT

using System;
using System.Numerics;

using Injure;
using Injure.Graphics;
using Injure.Graphics.Text;
using Injure.Input;
using Injure.Layers;

namespace TestGame;

public sealed class GameplayLayer : Layer {
	private readonly LayerTagSet tags = new([LayerTags.Gameplay]);

	private const float speed = 220f;
	private const float flashDecay = 2.5f;
	private Vector2 pos = new(200, 200);
	private float size = 40f;
	private float flash = 0f;
	private LiveText? helloWorldText;

	public override LayerFeatures Features => LayerFeatures.Render | LayerFeatures.Input;
	public override LayerTagSet Tags => tags;
	public override ActionProfile? ActionProfile => Actions.Profile;

	public override void OnEnter() {
		helloWorldText = TestGame.Text.Make(TestGame.TestFont, "ficelle\u0301 fffffi AVAVAV. ToToTo WaWaWa",
			new TextStyle(new FontOptions(PixelSize: 32), new Color32(255, 244, 216)));
	}

	public override void Update(in LayerTickContext ctx) {
		/*
		if (ctx.Actions.Buttons[Actions.Pause].Pressed)
			throw new NotImplementedException();
		*/

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
		helloWorldText?.Render(cv, new Vector2(32f, 32f));
		cv.Rect(new RectF(pos.X, pos.Y, size, size), new Color32((byte)(255 * (1f - flash)), 255, 255));
	}

	public override void OnLeave() {
		helloWorldText?.Dispose();
	}
}
