// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModEntrypointAttribute : Attribute;

public interface IModEntrypoint<in TGameApi> {
	ValueTask LoadAsync(IModLoadContext<TGameApi> context, CancellationToken ct);
	ValueTask LinkAsync(IModLinkContext<TGameApi> context, CancellationToken ct);
	ValueTask ActivateAsync(CancellationToken ct);
	ValueTask DeactivateAsync(CancellationToken ct);
	ValueTask UnloadAsync(CancellationToken ct);
}

public interface IModLiveReload {
	ValueTask<ModLiveStateBlob?> CaptureReloadStateAsync(CancellationToken ct);
	ValueTask RestoreReloadStateAsync(ModLiveStateBlob? state, CancellationToken ct);
}

public interface IModLoadContext<out TGameApi> {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	IOwnerScope OwnerScope { get; }
	ReloadGenerationScope GenerationScope { get; }
	CancellationToken UnloadToken { get; }
}

public interface IModLinkContext<out TGameApi> : IModLoadContext<TGameApi> {
	bool TryGetLoadedDependency(string ownerID, out LoadedDependencyInfo info);
}

public readonly record struct LoadedDependencyInfo(string OwnerID, Semver Version); // stub
