// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace Injure.Rendering;

/// <summary>
/// Fragment stage state for a render pipeline.
/// </summary>
/// <param name="ShaderModule">Shader module containing the fragment entry point.</param>
/// <param name="EntryPoint">Fragment entry point name.</param>
/// <param name="Targets">Color targets written by the fragment stage.</param>
public readonly record struct FragmentState(
	GPUShaderModuleHandle ShaderModule,
	string EntryPoint,
	ImmutableArray<ColorTargetState> Targets
);
