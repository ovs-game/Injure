// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;

namespace Injure.Rendering;

/// <summary>
/// Represents the primary render output for frame-based rendering.
/// </summary>
/// <remarks>
/// Color-only. Depth/stencil usage should use offscreen render targets instead.
/// </remarks>
public interface IRenderOutput : IDisposable {
	/// <summary>
	/// Current width in physical pixels.
	/// </summary>
	uint Width { get; }

	/// <summary>
	/// Current height in physical pixels.
	/// </summary>
	uint Height { get; }

	/// <summary>
	/// Color format.
	/// </summary>
	TextureFormat Format { get; }

	/// <summary>
	/// Re-queries output state after an external size or host-surface change.
	/// </summary>
	void Resized();

	/// <summary>
	/// Attempts to begin a new frame targeting this output.
	/// </summary>
	/// <param name="frame">
	/// On success, the newly created frame targeting this output.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a frame was begun successfully, and
	/// <see langword="false"/> if this frame should be skipped.
	/// </returns>
	/// <remarks>
	/// <para>
	/// Recoverable acquire failures should return <see langword="false"/>.
	/// Fatal failures should throw.
	/// </para>
	/// <para>
	/// On success, the returned <see cref="RenderFrame"/> owns the acquired
	/// output resources and is responsible for finalizing them when submitted or
	/// discarded.
	/// </para>
	/// </remarks>
	bool TryBeginFrame([NotNullWhen(true)] out RenderFrame? frame);
}
