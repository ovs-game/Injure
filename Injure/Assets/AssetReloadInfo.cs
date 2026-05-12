// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.Linq;

using Injure.Analyzers.Attributes;

namespace Injure.Assets;

/// <summary>
/// Stage of an asset reload operation where a failure occurred.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct AssetReloadFailureStage {
	/// <summary>Raw switch tag for <see cref="AssetReloadFailureStage"/>.</summary>
	public enum Case {
		/// <summary>
		/// The reload failed while preparing the replacement asset version.
		/// </summary>
		Prepare = 1,

		/// <summary>
		/// The reload failed while applying the prepared replacement version.
		/// </summary>
		Finalize,
	}
}

/// <summary>
/// Origin of an asset reload request.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct AssetReloadRequestOrigin {
	/// <summary>Raw switch tag for <see cref="AssetReloadRequestOrigin"/>.</summary>
	public enum Case {
		/// <summary>
		/// The reload was explicitly requested by caller code.
		/// </summary>
		Explicit = 1,

		/// <summary>
		/// The reload was caused by a watcher reporting a change in a published dependency.
		/// </summary>
		Dependency,
	}
}

/// <summary>
/// Describes a failed asset reload attempt.
/// </summary>
/// <param name="Asset">Asset key identifying the asset whose reload failed.</param>
/// <param name="TargetVersion">Asset version that the failed reload was trying to prepare or publish.</param>
/// <param name="Stage">Stage where the reload failed.</param>
/// <param name="Origin">Origin of the reload request.</param>
/// <param name="Trigger">Dependency that triggered the reload, if any.</param>
/// <param name="Exception">Exception that caused the reload failure.</param>
/// <remarks>
/// Failed reloads do not replace the currently live version. The old live version remains active
/// until a later reload succeeds or the asset/store is otherwise discarded.
/// </remarks>
public sealed record AssetReloadFailure(
	AssetKey Asset,
	ulong TargetVersion,
	AssetReloadFailureStage Stage,
	AssetReloadRequestOrigin Origin,
	IAssetDependency? Trigger,
	Exception Exception
);

/// <summary>
/// Result of applying queued asset reloads.
/// </summary>
/// <param name="AppliedCount">Number of prepared reloads that were successfully published.</param>
/// <param name="Failures">Finalize failures that occurred during the apply call.</param>
public readonly record struct AssetReloadReport(
	int AppliedCount,
	ImmutableArray<AssetReloadFailure> Failures
) {
	/// <summary>
	/// Throws if this report contains any reload failures.
	/// </summary>
	/// <exception cref="AggregateException">
	/// Thrown when <see cref="Failures"/> contains one or more entries. Contains
	/// all of the <see cref="AssetReloadFailure.Exception"/>s in them.
	/// </exception>
	public void ThrowIfFailed() {
		if (!Failures.IsDefaultOrEmpty)
			throw new AggregateException(Failures.Select(static f => f.Exception));
	}
}
