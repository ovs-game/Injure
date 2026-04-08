// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

/// <summary>
/// Storage for a surface descriptor and its platform-specific chained payloads.
/// </summary>
/// <remarks>
/// This type exists so a single stack-allocated value can hold both the root
/// <see cref="SurfaceDescriptor"/> and any platform-specific descriptor structs
/// chained from it, since the simple approach to returning a <see cref="SurfaceDescriptor"/>
/// would have a dangling pointer bug.
///
/// Implementations of <see cref="ISurfaceHost"/> should populate exactly the
/// fields required for the current platform and set up the descriptor chain so
/// that <see cref="SurfaceDescriptor"/> is ready to pass to
/// <see cref="WebGPU.InstanceCreateSurface(Instance*, SurfaceDescriptor*)"/>.
/// </remarks>
public struct SurfaceDescriptorContainer {
	public SurfaceDescriptor Desc;
	internal SurfaceDescriptorFromWindowsHWND WindowsHWND;
	internal SurfaceDescriptorFromMetalLayer MetalLayer;
	internal SurfaceDescriptorFromXlibWindow XlibWindow;
	internal SurfaceDescriptorFromWaylandSurface WaylandSurface;
}

/// <summary>
/// Describes a host-owned presentable surface.
/// </summary>
public unsafe interface ISurfaceHost {
	/// <summary>
	/// Creates a <see cref="SurfaceDescriptor"/> for the surface.
	/// </summary>
	/// <param name="container">
	/// Destination storage for the <see cref="SurfaceDescriptor"/> and any
	/// chained platform-specific descriptor structs.
	/// </param>
	void CreateSurfaceDesc(SurfaceDescriptorContainer *container);

	/// <summary>
	/// Gets the current drawable size of the host surface in physical pixels.
	/// </summary>
	/// <remarks>
	/// May differ from the logical size on high-DPI setups.
	/// </remarks>
	(uint Width, uint Height) GetDrawableSize();
}
