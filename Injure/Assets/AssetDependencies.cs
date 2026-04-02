// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.Assets;

/// <summary>
/// Describes an external dependency of a published asset version.
/// </summary>
/// <remarks>
/// <para>
/// Dependencies are collected while an asset version is being prepared and are
/// published together with that version. Dependency watchers may use them to
/// automatically queue reloads when the underlying external resource changes.
/// </para>
/// <para>
/// Implementations should be immutable value-like objects (e.g records) with
/// meaningful equality, since dependency tracking and watcher subscription are
/// based on value identity rather than object identity.
/// </para>
/// </remarks>
public interface IAssetDependency {}

// some builtin types
public sealed record FileAssetDependency(string FullPath) : IAssetDependency;
public sealed record EmbeddedAssetDependency(Assembly Assembly, string ResourcePath) : IAssetDependency;

/// <summary>
/// Records dependencies discovered while preparing an asset version.
/// </summary>
public interface IAssetDependencyCollector {
	void Add(IAssetDependency dep);
}

/// <summary>
/// Watches external dependencies of a specific type and reports when they change.
/// </summary>
/// <typeparam name="T">
/// Dependency type handled by this watcher.
/// </typeparam>
/// <remarks>
/// <para>
/// Dependency watchers are registered within an <see cref="AssetStore"/> and are
/// used to automatically queue reloads for published asset versions that depend
/// on matching external resources.
/// </para>
/// <para>
/// <see cref="Watch(T)"/> and <see cref="Unwatch(T)"/> are expected to be quick,
/// non-reentrant, and non-throwing in normal operation. <b>They must not call back
/// into the owning <see cref="AssetStore"/>.</b>
/// </para>
/// </remarks>
public interface IAssetDependencyWatcher<T> : IDisposable where T : IAssetDependency {
	/// <summary>
	/// Starts watching the specified dependency.
	/// </summary>
	/// <param name="dependency">Dependency to watch.</param>
	void Watch(T dependency);

	/// <summary>
	/// Stops watching the specified dependency.
	/// </summary>
	/// <param name="dependency">Dependency to stop watching.</param>
	void Unwatch(T dependency);

	/// <summary>
	/// Called when a watched dependency changes.
	/// </summary>
	event Action<T> Changed;
}
