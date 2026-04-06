// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public interface IRenderOutput : IDisposable {
	uint Width { get; }
	uint Height { get; }
	TextureFormat Format { get; }

	void Resized();
	bool TryBeginFrame([NotNullWhen(true)] out RenderFrame? frame);
}
