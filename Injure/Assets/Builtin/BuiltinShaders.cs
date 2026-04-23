// SPDX-License-Identifier: MIT

namespace Injure.Assets.Builtin;

public readonly record struct BuiltinShaderInfo(
	EngineResourceID ResourceID,
	string VSEntry,
	string FSEntry
);

public static class BuiltinShaders {
	public static readonly BuiltinShaderInfo Primitive2D = new(
		ResourceID: new EngineResourceID("shaders/primitive2d.wgsl"),
		VSEntry: "vs_main",
		FSEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2DColor = new(
		ResourceID: new EngineResourceID("shaders/textured2dColor.wgsl"),
		VSEntry: "vs_main",
		FSEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2DRMask = new(
		ResourceID: new EngineResourceID("shaders/textured2dRMask.wgsl"),
		VSEntry: "vs_main",
		FSEntry: "fs_main"
	);

	public static readonly BuiltinShaderInfo Textured2DSDF = new(
		ResourceID: new EngineResourceID("shaders/textured2dSDF.wgsl"),
		VSEntry: "vs_main",
		FSEntry: "fs_main"
	);
}
