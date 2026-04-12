// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public static class SamplerStates {
	public static readonly GPUSamplerCreateParams NearestClamp = new GPUSamplerCreateParams(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	public static readonly GPUSamplerCreateParams LinearClamp = new GPUSamplerCreateParams(
		AddressModeU: AddressMode.ClampToEdge,
		AddressModeV: AddressMode.ClampToEdge,
		AddressModeW: AddressMode.ClampToEdge,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);

	public static readonly GPUSamplerCreateParams NearestRepeat = new GPUSamplerCreateParams(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Nearest,
		MinFilter: FilterMode.Nearest,
		MipmapFilter: MipmapFilterMode.Nearest
	);

	public static readonly GPUSamplerCreateParams LinearRepeat = new GPUSamplerCreateParams(
		AddressModeU: AddressMode.Repeat,
		AddressModeV: AddressMode.Repeat,
		AddressModeW: AddressMode.Repeat,
		MagFilter: FilterMode.Linear,
		MinFilter: FilterMode.Linear,
		MipmapFilter: MipmapFilterMode.Linear
	);
}
