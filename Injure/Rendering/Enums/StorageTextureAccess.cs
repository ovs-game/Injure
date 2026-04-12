// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public enum StorageTextureAccess {
	None = 0,
	BindingNotUsed = 0,
	Undefined = 1,
	WriteOnly = 2,
	ReadOnly = 3,
	ReadWrite = 4
}
