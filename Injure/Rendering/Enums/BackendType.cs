// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public enum BackendType {
	None = 0,
	Undefined = 0,
	Null = 1,
	WebGPU = 2,
	D3D11 = 3,
	D3D12 = 4,
	Metal = 5,
	Vulkan = 6,
	OpenGL = 7,
	OpenGLES = 8
}
