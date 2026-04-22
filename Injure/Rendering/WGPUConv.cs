// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

internal static class WGPUConv {
	public static WGPUAddressMode ToWebGPUType(this AddressMode v) => (WGPUAddressMode)v;
	public static WGPUBlendFactor ToWebGPUType(this BlendFactor v) => (WGPUBlendFactor)v;
	public static WGPUBlendOperation ToWebGPUType(this BlendOperation v) => (WGPUBlendOperation)v;
	public static WGPUBufferBindingType ToWebGPUType(this BufferBindingType v) => (WGPUBufferBindingType)v;
	public static WGPUBufferUsage ToWebGPUType(this BufferUsage v) => (WGPUBufferUsage)v;
	public static WGPUColorWriteMask ToWebGPUType(this ColorWriteMask v) => (WGPUColorWriteMask)v;
	public static WGPUCompareFunction ToWebGPUType(this CompareFunction v) => (WGPUCompareFunction)v;
	public static WGPUCullMode ToWebGPUType(this CullMode v) => (WGPUCullMode)v;
	public static WGPUFilterMode ToWebGPUType(this FilterMode v) => (WGPUFilterMode)v;
	public static WGPUFrontFace ToWebGPUType(this FrontFace v) => (WGPUFrontFace)v;
	public static WGPUIndexFormat ToWebGPUType(this IndexFormat v) => (WGPUIndexFormat)v;
	public static WGPULoadOp ToWebGPUType(this LoadOp v) => (WGPULoadOp)v;
	public static WGPUMipmapFilterMode ToWebGPUType(this MipmapFilterMode v) => (WGPUMipmapFilterMode)v;
	public static WGPUPrimitiveTopology ToWebGPUType(this PrimitiveTopology v) => (WGPUPrimitiveTopology)v;
	public static WGPUSamplerBindingType ToWebGPUType(this SamplerBindingType v) => (WGPUSamplerBindingType)v;
	public static WGPUShaderStage ToWebGPUType(this ShaderStage v) => (WGPUShaderStage)v;
	public static WGPUStencilOperation ToWebGPUType(this StencilOperation v) => (WGPUStencilOperation)v;
	public static WGPUStorageTextureAccess ToWebGPUType(this StorageTextureAccess v) => (WGPUStorageTextureAccess)v;
	public static WGPUStoreOp ToWebGPUType(this StoreOp v) => (WGPUStoreOp)v;
	public static WGPUTextureAspect ToWebGPUType(this TextureAspect v) => (WGPUTextureAspect)v;
	public static WGPUTextureDimension ToWebGPUType(this TextureDimension v) => (WGPUTextureDimension)v;
	public static WGPUTextureFormat ToWebGPUType(this TextureFormat v) => (WGPUTextureFormat)v;
	public static WGPUTextureSampleType ToWebGPUType(this TextureSampleType v) => (WGPUTextureSampleType)v;
	public static WGPUTextureUsage ToWebGPUType(this TextureUsage v) => (WGPUTextureUsage)v;
	public static WGPUTextureViewDimension ToWebGPUType(this TextureViewDimension v) => (WGPUTextureViewDimension)v;
	public static WGPUVertexFormat ToWebGPUType(this VertexFormat v) => (WGPUVertexFormat)v;
	public static WGPUVertexStepMode ToWebGPUType(this VertexStepMode v) => (WGPUVertexStepMode)v;

	public static TextureAspect FromWebGPUType(this WGPUTextureAspect v) => TextureAspect.Enum.FromMirror(v);
	public static TextureFormat FromWebGPUType(this WGPUTextureFormat v) => TextureFormat.Enum.FromMirror(v);
	public static TextureUsage FromWebGPUType(this WGPUTextureUsage v) => TextureUsage.Flags.FromMirror(v);
	public static TextureViewDimension FromWebGPUType(this WGPUTextureViewDimension v) => TextureViewDimension.Enum.FromMirror(v);
}
