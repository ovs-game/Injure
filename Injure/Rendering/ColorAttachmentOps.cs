// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

public readonly record struct ColorAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	Color32 ClearValue
) {
	public static readonly ColorAttachmentOps Load = new ColorAttachmentOps(LoadOp.Load, StoreOp.Store, default);
	public static ColorAttachmentOps Clear(Color32 color) => new ColorAttachmentOps(LoadOp.Clear, StoreOp.Store, color);
}
