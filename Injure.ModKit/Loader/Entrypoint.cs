// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

using Injure.ModKit.Abstractions;

namespace Injure.ModKit.Loader;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ModEntrypointAttribute : Attribute;

public interface IModEntrypoint<in TGameApi> {
	ValueTask LoadAsync(IModLoadContext<TGameApi> ctx, CancellationToken ct);
	ValueTask LinkAsync(IModLinkContext<TGameApi> ctx, CancellationToken ct);
	ValueTask ActivateAsync(CancellationToken ct);
	ValueTask DeactivateAsync(CancellationToken ct);
	ValueTask UnloadAsync(CancellationToken ct);
}

public interface IModLoadContext<out TGameApi> {
	string OwnerID { get; }
	Semver Version { get; }
	TGameApi Api { get; }
	OwnerScope OwnerScope { get; }
	ReloadGenerationScope GenerationScope { get; }
	CancellationToken UnloadToken { get; }
}

public interface IModLinkContext<out TGameApi> : IModLoadContext<TGameApi> {
	bool TryGetLoadedDependency(string ownerID, out LoadedOwnerInfo info);
}

public readonly record struct LoadedOwnerInfo(string OwnerID, Semver Version); // stub
