// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

internal static class WGPUConv {
	//public static WGPUX ToWebGPUType(this X v) => (WGPUX)(int)v;
	//public static X FromWebGPUType(this WGPUX v) => (X)(int)v;

	public static WGPUAddressMode ToWebGPUType(this AddressMode v) => (WGPUAddressMode)(int)v;
	public static WGPUBlendFactor ToWebGPUType(this BlendFactor v) => (WGPUBlendFactor)(int)v;
	public static WGPUBlendOperation ToWebGPUType(this BlendOperation v) => (WGPUBlendOperation)(int)v;
	public static WGPUBufferBindingType ToWebGPUType(this BufferBindingType v) => (WGPUBufferBindingType)(int)v;
	public static WGPUBufferUsage ToWebGPUType(this BufferUsage v) => (WGPUBufferUsage)(ulong)v;
	public static WGPUColorWriteMask ToWebGPUType(this ColorWriteMask v) => (WGPUColorWriteMask)(ulong)v;
	public static WGPUCompareFunction ToWebGPUType(this CompareFunction v) => (WGPUCompareFunction)(int)v;
	public static WGPUCullMode ToWebGPUType(this CullMode v) => (WGPUCullMode)(int)v;
	public static WGPUFilterMode ToWebGPUType(this FilterMode v) => (WGPUFilterMode)(int)v;
	public static WGPUFrontFace ToWebGPUType(this FrontFace v) => (WGPUFrontFace)(int)v;
	public static WGPUIndexFormat ToWebGPUType(this IndexFormat v) => (WGPUIndexFormat)(int)v;
	public static WGPULoadOp ToWebGPUType(this LoadOp v) => (WGPULoadOp)(int)v;
	public static WGPUMipmapFilterMode ToWebGPUType(this MipmapFilterMode v) => (WGPUMipmapFilterMode)(int)v;
	public static WGPUPrimitiveTopology ToWebGPUType(this PrimitiveTopology v) => (WGPUPrimitiveTopology)(int)v;
	public static WGPUSamplerBindingType ToWebGPUType(this SamplerBindingType v) => (WGPUSamplerBindingType)(int)v;
	public static WGPUShaderStage ToWebGPUType(this ShaderStage v) => (WGPUShaderStage)(ulong)v;
	public static WGPUStencilOperation ToWebGPUType(this StencilOperation v) => (WGPUStencilOperation)(int)v;
	public static WGPUStorageTextureAccess ToWebGPUType(this StorageTextureAccess v) => (WGPUStorageTextureAccess)(int)v;
	public static WGPUStoreOp ToWebGPUType(this StoreOp v) => (WGPUStoreOp)(int)v;
	public static WGPUTextureAspect ToWebGPUType(this TextureAspect v) => (WGPUTextureAspect)(int)v;
	public static WGPUTextureDimension ToWebGPUType(this TextureDimension v) => (WGPUTextureDimension)(int)v;
	public static WGPUTextureFormat ToWebGPUType(this TextureFormat v) => (WGPUTextureFormat)(int)v;
	public static WGPUTextureSampleType ToWebGPUType(this TextureSampleType v) => (WGPUTextureSampleType)(int)v;
	public static WGPUTextureUsage ToWebGPUType(this TextureUsage v) => (WGPUTextureUsage)(ulong)v;
	public static WGPUTextureViewDimension ToWebGPUType(this TextureViewDimension v) => (WGPUTextureViewDimension)(int)v;
	public static WGPUVertexFormat ToWebGPUType(this VertexFormat v) => (WGPUVertexFormat)(int)v;
	public static WGPUVertexStepMode ToWebGPUType(this VertexStepMode v) => (WGPUVertexStepMode)(int)v;

	public static TextureAspect FromWebGPUType(this WGPUTextureAspect v) => (TextureAspect)(int)v;
	public static TextureFormat FromWebGPUType(this WGPUTextureFormat v) => (TextureFormat)(int)v;
	public static TextureUsage FromWebGPUType(this WGPUTextureUsage v) => (TextureUsage)(ulong)v;
	public static TextureViewDimension FromWebGPUType(this WGPUTextureViewDimension v) => (TextureViewDimension)(int)v;
}
