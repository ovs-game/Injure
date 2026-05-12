// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Numerics;

using Injure.Analyzers.Attributes;

namespace Injure.Input;

[ClosedEnum]
public readonly partial struct AxisDeadzoneKind {
	public enum Case {
		None,
		Threshold,
		Scaled,
	}
}

public readonly record struct AxisDeadzone(
	AxisDeadzoneKind Kind,
	float Inner,
	float Outer = 1f
) {
	public static readonly AxisDeadzone None = default;
	public static AxisDeadzone Threshold(float inner) => new(AxisDeadzoneKind.Threshold, inner, 1f);
	public static AxisDeadzone Scaled(float inner, float outer = 1f) => new(AxisDeadzoneKind.Scaled, inner, outer);

	public float Apply(float v) {
		if (Kind == AxisDeadzoneKind.None)
			return v;

		float av = MathF.Abs(v);
		if (av <= Inner)
			return 0f;

		if (Kind == AxisDeadzoneKind.Threshold)
			return Math.Clamp(v, -Outer, Outer);
		if (Outer <= Inner)
			return 0.0f;
		float mag = (av - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return v < 0f ? -mag : mag;
	}
}

[ClosedEnum]
public readonly partial struct Axis2DDeadzoneKind {
	public enum Case {
		None,
		Radial,
		ScaledRadial,
		Axial,
		ScaledAxial,
	}
}

public readonly record struct Axis2DDeadzone(
	Axis2DDeadzoneKind Kind,
	float Inner,
	float Outer = 1f
) {
	public static readonly Axis2DDeadzone None = default;
	public static Axis2DDeadzone Radial(float inner, float outer = 1f) => new(Axis2DDeadzoneKind.Radial, inner, outer);
	public static Axis2DDeadzone ScaledRadial(float inner, float outer = 1f) => new(Axis2DDeadzoneKind.ScaledRadial, inner, outer);
	public static Axis2DDeadzone Axial(float inner, float outer = 1f) => new(Axis2DDeadzoneKind.Axial, inner, outer);
	public static Axis2DDeadzone ScaledAxial(float inner, float outer = 1f) => new(Axis2DDeadzoneKind.ScaledAxial, inner, outer);

	public Vector2 Apply(Vector2 v) {
		if (Kind == Axis2DDeadzoneKind.None)
			return v;
		if (Outer <= Inner)
			return Vector2.Zero;
		return Kind.Tag switch {
			Axis2DDeadzoneKind.Case.Radial => applyRadial(v, scaled: false),
			Axis2DDeadzoneKind.Case.ScaledRadial => applyRadial(v, scaled: true),
			Axis2DDeadzoneKind.Case.Axial => new Vector2(applyAxis(v.X, scaled: false), applyAxis(v.Y, scaled: false)),
			Axis2DDeadzoneKind.Case.ScaledAxial => new Vector2(applyAxis(v.X, scaled: true), applyAxis(v.Y, scaled: true)),
			_ => throw new UnreachableException(),
		};
	}

	private Vector2 applyRadial(Vector2 v, bool scaled) {
		float len = v.Length();
		if (len <= Inner)
			return Vector2.Zero;

		Vector2 dir = v / len;
		if (!scaled)
			return clampMagnitude(v, Outer);
		float mag = (len - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return dir * mag;
	}

	private float applyAxis(float x, bool scaled) {
		float ax = MathF.Abs(x);
		if (ax <= Inner)
			return 0f;

		if (!scaled)
			return Math.Clamp(x, -Outer, Outer);
		float mag = (ax - Inner) / (Outer - Inner);
		mag = Math.Clamp(mag, 0f, 1f);
		return x < 0f ? -mag : mag;
	}

	internal static Vector2 ClampMagnitude1(Vector2 v) => clampMagnitude(v, 1.0f);

	private static Vector2 clampMagnitude(Vector2 v, float max) {
		float lenSq = v.LengthSquared();
		if (lenSq <= max * max)
			return v;
		if (lenSq <= 0f)
			return Vector2.Zero;
		return v * (max / MathF.Sqrt(lenSq));
	}
}
