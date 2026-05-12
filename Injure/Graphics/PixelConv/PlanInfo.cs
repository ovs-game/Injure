// SPDX-License-Identifier: MIT

using Injure.Analyzers.Attributes;

namespace Injure.Graphics.PixelConv;

/// <summary>
/// Describes the broad execution strategy that was chosen for a conversion plan.
/// </summary>
[ClosedEnum(DefaultIsInvalid = true)]
public readonly partial struct PlanExecutionPath {
	/// <summary>Raw switch tag for <see cref="PlanExecutionPath"/>.</summary>
	public enum Case {
		/// <summary>
		/// There is no conversion; a byte-for-byte copy will be done.
		/// </summary>
		/// <remarks>
		/// Used when input/output formats match and no additional channel override/etc.
		/// transforms are needed.
		/// </remarks>
		Memcpy = 1,

		/// <summary>
		/// The conversion uses a dedicated path for the selected format family.
		/// </summary>
		DedicatedKernel,

		/// <summary>
		/// The conversion has no dedicated path and uses the generic fallback converter.
		/// </summary>
		GenericKernel,
	}
}

/// <summary>
/// Describes the backend selected for a conversion.
/// </summary>
/// <remarks>
/// Informational only; backend selection is not a stable API and may vary
/// by runtime, hardware, library version, or even the input.
/// </remarks>
[ClosedEnum]
public readonly partial struct PlanBackend {
	/// <summary>Raw switch tag for <see cref="PlanBackend"/>.</summary>
	public enum Case {
		/// <summary>
		/// There is no conversion; see <see cref="PlanExecutionPath.Memcpy"/>.
		/// </summary>
		None,

		/// <summary>
		/// The conversion uses an AVX2 implementation.
		/// </summary>
		AVX2,

		/// <summary>
		/// The conversion uses an SSSE3 implementation.
		/// </summary>
		SSSE3,

		/// <summary>
		/// The conversion uses an SSE2 implementation.
		/// </summary>
		SSE2,

		/// <summary>
		/// The conversion uses an ARM64 Advanced SIMD implementation.
		/// </summary>
		AdvSIMD,

		/// <summary>
		/// The conversion uses a non-hardware-vectorized, scalar implementation.
		/// </summary>
		Scalar,
	}
}

/// <summary>
/// Provides informational details about how a <see cref="PixelConversionPlan"/>
/// will execute.
/// </summary>
/// <remarks>
/// Informational only; backend selection is not a stable API and may vary
/// by runtime, hardware, library version, or even the input.
/// </remarks>
public readonly struct PlanInfo {
	/// <summary>
	/// Broad execution strategy that was selected for this plan.
	/// </summary>
	public PlanExecutionPath ExecutionPath { get; }

	/// <summary>
	/// Backend selected for this plan.
	/// </summary>
	public PlanBackend Backend { get; }

	internal PlanInfo(PlanExecutionPath executionPath, PlanBackend backend) {
		ExecutionPath = executionPath;
		Backend = backend;
	}
}
