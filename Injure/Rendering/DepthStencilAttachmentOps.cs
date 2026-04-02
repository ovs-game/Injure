// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

public readonly record struct DepthStencilAttachmentOps(
	LoadOp DepthLoadOp,
	StoreOp DepthStoreOp,
	float DepthClearValue,
	LoadOp StencilLoadOp,
	StoreOp StencilStoreOp,
	uint StencilClearValue
) {
	public static readonly DepthStencilAttachmentOps Load = new DepthStencilAttachmentOps(
		DepthLoadOp: LoadOp.Load,
		DepthStoreOp: StoreOp.Store,
		DepthClearValue: 1f,
		StencilLoadOp: LoadOp.Load,
		StencilStoreOp: StoreOp.Store,
		StencilClearValue: 0
	);

	public static DepthStencilAttachmentOps Clear(float depthClear = 1f, uint stencilClear = 0) => new DepthStencilAttachmentOps(
		DepthLoadOp: LoadOp.Clear,
		DepthStoreOp: StoreOp.Store,
		DepthClearValue: depthClear,
		StencilLoadOp: LoadOp.Clear,
		StencilStoreOp: StoreOp.Store,
		StencilClearValue: stencilClear
	);

	public static DepthStencilAttachmentOps ClearDepth(float depthClear = 1f) => new DepthStencilAttachmentOps(
		DepthLoadOp: LoadOp.Clear,
		DepthStoreOp: StoreOp.Store,
		DepthClearValue: depthClear,
		StencilLoadOp: LoadOp.Load,
		StencilStoreOp: StoreOp.Store,
		StencilClearValue: 0
	);

	public static DepthStencilAttachmentOps ClearStencil(uint stencilClear = 0) => new DepthStencilAttachmentOps(
		DepthLoadOp: LoadOp.Load,
		DepthStoreOp: StoreOp.Store,
		DepthClearValue: 1f,
		StencilLoadOp: LoadOp.Clear,
		StencilStoreOp: StoreOp.Store,
		StencilClearValue: stencilClear
	);
}
