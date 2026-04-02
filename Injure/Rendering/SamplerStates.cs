// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

public static class SamplerStates {
	public static readonly GPUSamplerCreateParams NearestClamp = new GPUSamplerCreateParams(
		MinFilter: FilterMode.Nearest,
		MagFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest,
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge
	);

	public static readonly GPUSamplerCreateParams LinearClamp = new GPUSamplerCreateParams(
		MinFilter: FilterMode.Linear,
		MagFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear,
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge
	);

	public static readonly GPUSamplerCreateParams NearestRepeat = new GPUSamplerCreateParams(
		MinFilter: FilterMode.Nearest,
		MagFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest,
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat
	);

	public static readonly GPUSamplerCreateParams LinearRepeat = new GPUSamplerCreateParams(
		MinFilter: FilterMode.Linear,
		MagFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear,
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat
	);
}
