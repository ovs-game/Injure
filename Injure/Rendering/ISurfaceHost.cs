// SPDX-License-Identifier: MIT

using WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Storage for a surface descriptor and its platform-specific chained payloads.
/// </summary>
/// <remarks>
/// This type exists so a single stack-allocated value can hold both the root
/// <see cref="WGPUSurfaceDescriptor"/> and any platform-specific descriptor structs
/// chained from it, since the simple approach to returning a <see cref="WGPUSurfaceDescriptor"/>
/// would have a dangling pointer bug.
///
/// Implementors of <see cref="ISurfaceHost"/> should populate exactly the
/// fields required for the current platform and set up the descriptor chain so
/// that <see cref="WGPUSurfaceDescriptor"/> is ready to pass to
/// <see cref="WebGPU.WebGPU.wgpuInstanceCreateSurface(WGPUInstance, WGPUSurfaceDescriptor*)"/>.
/// </remarks>
public struct WGPUSurfaceDescriptorContainer {
	public WGPUSurfaceDescriptor Desc;
	internal WGPUSurfaceSourceWindowsHWND WindowsHWND;
	internal WGPUSurfaceSourceMetalLayer MetalLayer;
	internal WGPUSurfaceSourceXlibWindow XlibWindow;
	internal WGPUSurfaceSourceWaylandSurface WaylandSurface;
}

/// <summary>
/// Describes a host-owned presentable surface.
/// </summary>
public unsafe interface ISurfaceHost {
	/// <summary>
	/// Creates a <see cref="WGPUSurfaceDescriptor"/> for the surface.
	/// </summary>
	/// <param name="container">
	/// Destination storage for the <see cref="WGPUSurfaceDescriptor"/> and any
	/// chained platform-specific descriptor structs.
	/// </param>
	void CreateSurfaceDesc(WGPUSurfaceDescriptorContainer *container);

	/// <summary>
	/// Gets the current drawable size of the host surface in physical pixels.
	/// </summary>
	/// <remarks>
	/// May differ from the logical size on high-DPI setups.
	/// </remarks>
	(uint Width, uint Height) GetDrawableSize();
}
