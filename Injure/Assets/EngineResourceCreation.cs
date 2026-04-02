// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public enum EngineResourceSourceResultKind {
	NotHandled,
	Success,
	Error
}

public readonly record struct EngineResourceSourceResult(
	EngineResourceSourceResultKind Kind,
	EngineResourceData? Data = null,
	Exception? Exception = null
) {
	public static EngineResourceSourceResult NotHandled() => new(EngineResourceSourceResultKind.NotHandled);
	public static EngineResourceSourceResult Success(EngineResourceData data) => new(EngineResourceSourceResultKind.Success, data);
	public static EngineResourceSourceResult Error(Exception ex) => new(EngineResourceSourceResultKind.Error, null, ex);
}

public interface IEngineResourceSource {
	EngineResourceSourceResult TrySource(EngineResourceID id);
}
