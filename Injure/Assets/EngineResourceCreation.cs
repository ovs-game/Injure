// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Assets;

/// <summary>
/// Result kind returned by engine resource sources.
/// </summary>
[ClosedEnum]
public readonly partial struct EngineResourceSourceResultKind {
	/// <summary>Raw switch tag for <see cref="EngineResourceSourceResultKind"/>.</summary>
	public enum Case {
		/// <summary>
		/// The source does not provide the requested resource.
		/// </summary>
		NotHandled,

		/// <summary>
		/// The source successfully provided the requested resource.
		/// </summary>
		Success
	}
}

/// <summary>
/// Result returned by an engine resource source.
/// </summary>
/// <param name="Kind">Result kind.</param>
/// <param name="Data">On success, the data for the resource.</param>
public readonly record struct EngineResourceSourceResult(
	EngineResourceSourceResultKind Kind,
	EngineResourceData? Data = null
) {
	/// <summary>Factory for a <see cref="EngineResourceSourceResultKind.NotHandled"/> result.</summary>
	public static EngineResourceSourceResult NotHandled() => new(EngineResourceSourceResultKind.NotHandled);

	/// <summary>Factory for a <see cref="EngineResourceSourceResultKind.Success"/> result.</summary>
	public static EngineResourceSourceResult Success(EngineResourceData data) => new(EngineResourceSourceResultKind.Success, data);
}

/// <summary>
/// Creates engine resources. Not to be confused with <see cref="IAssetSource"/>;
/// engine resources have no multi-step creation pipeline, the source is responsible
/// for the full creation process.
/// </summary>
public interface IEngineResourceSource {
	/// <summary>Attempts to create an engine resource.</summary>
	/// <param name="id">Resource ID.</param>
	EngineResourceSourceResult TryCreate(EngineResourceID id);
}
