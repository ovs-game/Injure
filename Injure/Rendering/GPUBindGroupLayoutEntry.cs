// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

/// <summary>
/// Describes one binding entry in a bind group layout.
/// </summary>
/// <param name="Binding">Binding index within the layout.</param>
/// <param name="Visibility">Shader stages allowed to access the binding.</param>
/// <param name="Layout">Binding layout for the bound resource.</param>
public readonly record struct GPUBindGroupLayoutEntry(
	uint Binding,
	ShaderStage Visibility,
	GPUBindingLayout Layout
);

/// <summary>
/// Base type for binding layout descriptions used by
/// <see cref="WebGPUDevice.CreateBindGroupLayout(System.ReadOnlySpan{GPUBindGroupLayoutEntry})"/>.
/// </summary>
public abstract record GPUBindingLayout;

/// <summary>
/// Describes a buffer binding layout.
/// </summary>
/// <param name="Type">Buffer binding type.</param>
/// <param name="HasDynamicOffset">Whether the binding uses a dynamic offset.</param>
/// <param name="MinBindingSize">Minimum buffer range size in bytes required by the binding.</param>
public sealed record GPUBufferBindingLayout(
	BufferBindingType Type,
	bool HasDynamicOffset = false,
	ulong MinBindingSize = 0
) : GPUBindingLayout;

/// <summary>
/// Describes a sampler binding layout.
/// </summary>
/// <param name="Type">Sampler binding type.</param>
public sealed record GPUSamplerBindingLayout(
	SamplerBindingType Type
) : GPUBindingLayout;

/// <summary>
/// Describes a storage texture binding layout.
/// </summary>
/// <param name="Access">Storage access mode.</param>
/// <param name="Format">Storage texture format.</param>
/// <param name="ViewDimension">Expected texture view dimension.</param>
public sealed record GPUStorageTextureBindingLayout(
	StorageTextureAccess Access,
	TextureFormat Format,
	TextureViewDimension ViewDimension
) : GPUBindingLayout;

/// <summary>
/// Describes a sampled texture binding layout.
/// </summary>
/// <param name="SampleType">Texture sample type.</param>
/// <param name="ViewDimension">Expected texture view dimension.</param>
/// <param name="Multisampled">Whether the bound view is multisampled.</param>
public sealed record GPUTextureBindingLayout(
	TextureSampleType SampleType,
	TextureViewDimension ViewDimension,
	bool Multisampled = false
) : GPUBindingLayout;
