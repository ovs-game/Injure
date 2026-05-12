// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Analyzers.Attributes;

namespace Injure.ModKit.Abstractions;

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModCodeHotReloadLevel {
	public enum Case {
		None = 1,
		Restartless,
		Live,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModAssetManagementKind {
	public enum Case {
		None = 1,
		Tracked,
		Manual,
		Untracked,
	}
}

[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct ModPatchingBackend {
	public enum Case {
		None = 1,
		Managed,
		Unmanaged,
	}
}

public sealed record ModOrderManifest {
	public required IReadOnlyList<string> After { get; init; }
	public required IReadOnlyList<string> Before { get; init; }
	public static ModOrderManifest Empty { get; } = new() {
		After = Array.Empty<string>(),
		Before = Array.Empty<string>(),
	};
}

public readonly record struct ModAssetsManifest(
	ModAssetManagementKind ManagementKind,
	string? Root
);

public abstract record ModManifest {
	public required string OwnerID { get; init; }
	public required Semver Version { get; init; }
	public required IReadOnlyDictionary<string, Semver> Dependencies { get; init; }
	public required ModOrderManifest Order { get; init; }
	public required ModAssetsManifest Assets { get; init; }
}

public sealed record CodeModManifest : ModManifest {
	public required string EntryAssembly { get; init; }
	public required ModCodeHotReloadLevel CodeHotReload { get; init; }
	public required ModPatchingBackend Patching { get; init; }
}

public sealed record ContentModManifest : ModManifest {
}
