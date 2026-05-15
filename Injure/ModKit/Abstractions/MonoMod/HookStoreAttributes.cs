// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions.MonoMod;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModHookRootAttribute : Attribute {
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModRawHookRootAttribute : Attribute {
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModHookTargetStoreAttribute(Type storeType) : Attribute {
	public Type StoreType { get; } = storeType;
}
