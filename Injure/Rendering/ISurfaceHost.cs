// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

public struct SurfaceDescriptorContainer {
	public SurfaceDescriptor Desc;
	internal SurfaceDescriptorFromWindowsHWND WindowsHWND;
	internal SurfaceDescriptorFromMetalLayer MetalLayer;
	internal SurfaceDescriptorFromXlibWindow XlibWindow;
	internal SurfaceDescriptorFromWaylandSurface WaylandSurface;
}

public unsafe interface ISurfaceHost {
	void CreateSurfaceDesc(SurfaceDescriptorContainer *container);
	(uint Width, uint Height) GetDrawableSize();
}
