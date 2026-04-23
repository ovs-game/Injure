// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public readonly record struct ColorAttachmentOps(
	LoadOp LoadOp,
	StoreOp StoreOp,
	Color32 ClearValue
) {
	public static readonly ColorAttachmentOps Load = new(LoadOp.Load, StoreOp.Store, default);
	public static ColorAttachmentOps Clear(Color32 color) => new(LoadOp.Clear, StoreOp.Store, color);
}
