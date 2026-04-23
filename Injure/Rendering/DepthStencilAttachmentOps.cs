// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public readonly record struct DepthAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	float ClearValue
) {
	public static readonly DepthAttachmentOps Load = new(
		LoadOp: LoadOp.Load,
		StoreOp: StoreOp.Store,
		ClearValue: 1f
	);

	public static DepthAttachmentOps Clear(float value) => new(
		LoadOp: LoadOp.Clear,
		StoreOp: StoreOp.Store,
		ClearValue: value
	);
}

public readonly record struct StencilAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	uint ClearValue
) {
	public static readonly StencilAttachmentOps Load = new(
		LoadOp: LoadOp.Load,
		StoreOp: StoreOp.Store,
		ClearValue: 0
	);

	public static StencilAttachmentOps Clear(uint value) => new(
		LoadOp: LoadOp.Clear,
		StoreOp: StoreOp.Store,
		ClearValue: value
	);
}
