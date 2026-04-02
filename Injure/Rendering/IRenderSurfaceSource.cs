// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

namespace Injure.Rendering;

public struct SurfaceDescriptorContainer {
	public SurfaceDescriptor Desc;
	public SurfaceDescriptorFromWindowsHWND WindowsHWND;
	public SurfaceDescriptorFromMetalLayer MetalLayer;
	public SurfaceDescriptorFromXlibWindow XlibWindow;
	public SurfaceDescriptorFromWaylandSurface WaylandSurface;
}

public unsafe interface IRenderSurfaceSource {
	void CreateSurfaceDesc(SurfaceDescriptorContainer *container);
	(uint Width, uint Height) GetDrawableSize();
}
