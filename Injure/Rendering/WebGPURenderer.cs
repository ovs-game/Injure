// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

using Buffer = Silk.NET.WebGPU.Buffer;
using Surface = Silk.NET.WebGPU.Surface;
using Texture = Silk.NET.WebGPU.Texture;

using static Injure.Rendering.WebGPUException;

namespace Injure.Rendering;

/// <summary>
/// Policy for selecting a surface present mode in <see cref="WebGPURenderer"/>.
/// </summary>
public enum SurfacePresentModePolicy {
	/// <summary>
	/// Prefer <see cref="PresentMode.Mailbox"/>, fall back to <see cref="PresentMode.Fifo"/>
	/// if not present.
	/// </summary>
	/// <remarks>
	/// Tear-free.
	/// </remarks>
	AutoMailbox,

	/// <summary>
	/// Prefer <see cref="PresentMode.FifoRelaxed"/>, fall back to <see cref="PresentMode.Mailbox"/>
	/// and then <see cref="PresentMode.Fifo"/> if not present.
	/// </summary>
	/// <remarks>
	/// Normally tear-free; may tear if a frame remains on the frontbuffer for more than one vblank.
	/// </remarks>
	AutoRelaxedFifo,

	/// <summary>
	/// Prefer <see cref="PresentMode.Immediate"/>, fall back to <see cref="PresentMode.Mailbox"/>,
	/// then <see cref="PresentMode.FifoRelaxed">, and finally <see cref="PresentMode.Fifo"/>
	/// if not present.
	/// </summary>
	/// <remarks>
	/// May tear. Lowest latency.
	/// </remarks>
	AutoImmediate
}

/// <summary>
/// Base WebGPU renderer.
///
/// Owns the WebGPU instance, surface, adapter, device, queue, surface
/// configuration, renderer-global bind groups, and shared bind group layouts.
/// Almost all other rendering objects are created from this type and should be
/// treated as invalid once it is disposed.
/// </summary>
public sealed unsafe class WebGPURenderer : IDisposable {
	// ==========================================================================
	// internal objects / properties
	internal readonly WebGPU webgpu;

	private readonly IRenderSurfaceSource surfaceSource;
	private readonly Instance *instance;
	private readonly Adapter *adapter;
	private readonly Device *device;
	private readonly Queue *queue;

	private readonly GPUBuffer globalsUniformBuffer;
	private readonly BindGroupLayout *globalsUniformBindGroupLayout;
	private readonly BindGroupLayout *texBindGroupLayout;
	private readonly BindGroup *globalsUniformBindGroup;

	private readonly SurfacePresentModePolicy presentPolicy;
	private Surface *surface;
	private TextureFormat surfaceFormat;
	private PresentMode surfacePresentMode;
	private SurfaceConfiguration surfaceConfig;

	private readonly GPUBindGroupLayoutRef globalsUniformBindGroupLayoutWrap;
	private readonly GPUBindGroupLayoutRef textureBindGroupLayoutWrap;
	private readonly GPUBindGroupRef globalsUniformBindGroupWrap;

	private bool disposed = false;

	// ==========================================================================
	// public properties and ctor

	/// <summary>
	/// Current drawable width of the backbuffer surface in pixels.
	/// </summary>
	public uint Width { get; private set; }

	/// <summary>
	/// Current drawable height of the backbuffer surface in pixels.
	/// </summary>
	public uint Height { get; private set; }

	/// <summary>
	/// Color format used by the backbuffer surface.
	/// </summary>
	public TextureFormat BackbufferFormat {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return surfaceFormat;
		}
	}

	/// <summary>
	/// Ref to the renderer-global bind group layout for the globals uniform.
	/// </summary>
	public GPUBindGroupLayoutRef GlobalsUniformBindGroupLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return globalsUniformBindGroupLayoutWrap;
		}
	}

	/// <summary>
	/// Ref to the standard bind group layout used for the sampler + texture.
	/// </summary>
	public GPUBindGroupLayoutRef TextureBindGroupLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return textureBindGroupLayoutWrap;
		}
	}

	/// <summary>
	/// Ref to the renderer-global bind group containing the globals uniform.
	/// </summary>
	public GPUBindGroupRef GlobalsUniformBindGroup {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return globalsUniformBindGroupWrap;
		}
	}

	/// <summary>
	/// Creates a renderer from the given <see cref="WebGPUBootstrapResult"/>.
	/// </summary>
	public WebGPURenderer(WebGPUBootstrapResult bootstrap, SurfacePresentModePolicy presentPolicy) {
		webgpu = WebGPU.GetApi();
		surfaceSource = bootstrap.SurfaceSource;
		instance = bootstrap.Instance;
		surface = bootstrap.Surface;
		adapter = bootstrap.Adapter;
		device = bootstrap.Device;
		queue = bootstrap.Queue;
		this.presentPolicy = presentPolicy;
		surfaceFormat = getSurfaceFormat();
		surfacePresentMode = getSurfacePresentMode();
		queryAndResize(updateProj: false);
		mkGlobalsUniformBindGroup(out globalsUniformBuffer, out globalsUniformBindGroupLayout, out globalsUniformBindGroup);
		globalsUniformBindGroupLayoutWrap = new GPUBindGroupLayoutRef(globalsUniformBindGroupLayout);
		globalsUniformBindGroupWrap = new GPUBindGroupRef(globalsUniformBindGroup);
		updateProjection(Width, Height);
		texBindGroupLayout = mkTexBindGroupLayout();
		textureBindGroupLayoutWrap = new GPUBindGroupLayoutRef(texBindGroupLayout);
	}

	// ==========================================================================
	// resource creation
	private TextureFormat getSurfaceFormat() {
		SurfaceCapabilities caps;
		webgpu.SurfaceGetCapabilities(surface, adapter, &caps);
		try {
			if (caps.FormatCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported formats");
			// wgpu says the first format is the most preferred one
			return caps.Formats[0];
		} finally {
			webgpu.SurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private PresentMode getSurfacePresentMode() {
		SurfaceCapabilities caps;
		webgpu.SurfaceGetCapabilities(surface, adapter, &caps);
		try {
			if (caps.PresentModeCount == 0)
				throw new WebGPUException("SurfaceGetCapabilities", "surface doesn't report any supported present modes");
			ReadOnlySpan<PresentMode> modes = new ReadOnlySpan<PresentMode>(caps.PresentModes, (int)caps.PresentModeCount);
			bool haverelaxed = modes.Contains(PresentMode.FifoRelaxed);
			bool havemailbox = modes.Contains(PresentMode.Mailbox);
			bool haveimmediate = modes.Contains(PresentMode.Immediate);
			switch (presentPolicy) {
			case SurfacePresentModePolicy.AutoMailbox:
				return havemailbox ? PresentMode.Mailbox : PresentMode.Fifo;
			case SurfacePresentModePolicy.AutoRelaxedFifo:
				if (haverelaxed)
					return PresentMode.FifoRelaxed;
				return havemailbox ? PresentMode.Mailbox : PresentMode.Fifo;
			case SurfacePresentModePolicy.AutoImmediate:
				if (haveimmediate)
					return PresentMode.Immediate;
				if (havemailbox)
					return PresentMode.Mailbox;
				return haverelaxed ? PresentMode.FifoRelaxed : PresentMode.Fifo;
			default:
				throw new UnreachableException();
			}
		} finally {
			webgpu.SurfaceCapabilitiesFreeMembers(caps);
		}
	}

	private SurfaceConfiguration getSurfaceConfig(uint width, uint height) {
		SurfaceConfiguration cfg = default;
		cfg.Device = device;
		cfg.Format = surfaceFormat;
		cfg.Usage = TextureUsage.RenderAttachment;
		cfg.Width = width;
		cfg.Height = height;
		cfg.PresentMode = PresentMode.Fifo;
		cfg.AlphaMode = CompositeAlphaMode.Auto;
		return cfg;
	}

	private void mkGlobalsUniformBindGroup(out GPUBuffer globalsUniformBuffer, out BindGroupLayout *globalsUniformBindGroupLayout, out BindGroup *globalsUniformBindGroup) {
		globalsUniformBuffer = CreateBuffer((ulong)sizeof(GlobalsUniform), BufferUsage.Uniform | BufferUsage.CopyDst);
		BindGroupLayoutEntry *bglEntries = stackalloc BindGroupLayoutEntry[1];
		bglEntries[0] = new BindGroupLayoutEntry {
			Binding = 0,
			Visibility = ShaderStage.Vertex,
			Buffer = new BufferBindingLayout {
				Type = BufferBindingType.Uniform,
				HasDynamicOffset = false,
				MinBindingSize = (ulong)sizeof(GlobalsUniform)
			}
		};
		BindGroupLayoutDescriptor bglDesc = new BindGroupLayoutDescriptor {
			EntryCount = 1,
			Entries = bglEntries
		};
		globalsUniformBindGroupLayout = Check(webgpu.DeviceCreateBindGroupLayout(device, &bglDesc));
		BindGroupEntry *bgEntries = stackalloc BindGroupEntry[1];
		bgEntries[0] = new BindGroupEntry {
			Binding = 0,
			Buffer = globalsUniformBuffer.Buffer,
			Offset = 0,
			Size = globalsUniformBuffer.Size
		};
		BindGroupDescriptor bgDesc = new BindGroupDescriptor {
			Layout = globalsUniformBindGroupLayout,
			EntryCount = 1,
			Entries = bgEntries
		};
		globalsUniformBindGroup = Check(webgpu.DeviceCreateBindGroup(device, &bgDesc));
	}

	private BindGroupLayout *mkTexBindGroupLayout() {
		BindGroupLayoutEntry *bglEntries = stackalloc BindGroupLayoutEntry[2];
		bglEntries[0] = new BindGroupLayoutEntry {
			Binding = 0,
			Visibility = ShaderStage.Fragment,
			Texture = new TextureBindingLayout {
				SampleType = TextureSampleType.Float,
				ViewDimension = TextureViewDimension.Dimension2D, // TODO: support for array textures
				Multisampled = false
			}
		};
		bglEntries[1] = new BindGroupLayoutEntry {
			Binding = 1,
			Visibility = ShaderStage.Fragment,
			Sampler = new SamplerBindingLayout {
				Type = SamplerBindingType.Filtering
			}
		};
		BindGroupLayoutDescriptor bglDesc = new BindGroupLayoutDescriptor {
			EntryCount = 2,
			Entries = bglEntries
		};
		return Check(webgpu.DeviceCreateBindGroupLayout(device, &bglDesc));
	}

	private bool tryGetCurrentTex(out SurfaceTexture outTex) {
		SurfaceTexture tex;
		webgpu.SurfaceGetCurrentTexture(surface, &tex);
		switch (tex.Status) {
		case SurfaceGetCurrentTextureStatus.Success:
			outTex = tex;
			return true;
		case SurfaceGetCurrentTextureStatus.Timeout:
			outTex = default;
			return false;
		case SurfaceGetCurrentTextureStatus.Outdated:
			queryAndResize();
			webgpu.SurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return tex.Status == SurfaceGetCurrentTextureStatus.Success;
		case SurfaceGetCurrentTextureStatus.Lost:
			webgpu.SurfaceRelease(surface);
			SurfaceDescriptorContainer sdc;
			surfaceSource.CreateSurfaceDesc(&sdc);
			surface = webgpu.InstanceCreateSurface(instance, &sdc.Desc);
			surfaceFormat = getSurfaceFormat();
			surfacePresentMode = getSurfacePresentMode();
			queryAndResize();
			webgpu.SurfaceGetCurrentTexture(surface, &tex);
			outTex = tex;
			return tex.Status == SurfaceGetCurrentTextureStatus.Success;
		case SurfaceGetCurrentTextureStatus.DeviceLost:
			throw new WebGPUException("SurfaceGetCurrentTexture", "got DeviceLost, bailing out");
		case SurfaceGetCurrentTextureStatus.OutOfMemory:
			throw new OutOfMemoryException("WebGPU: SurfaceGetCurrentTexture out of memory");
		default:
			throw new WebGPUException("SurfaceGetCurrentTexture", tex.Status.ToString());
		}
	}

	private GPUBindGroup createTexViewPlusSamplerBindGroup(TextureView *tv, Sampler *sampler) {
		BindGroupEntry *entries = stackalloc BindGroupEntry[2];
		entries[0] = new BindGroupEntry {
			Binding = 0,
			TextureView = tv
		};
		entries[1] = new BindGroupEntry {
			Binding = 1,
			Sampler = sampler
		};

		BindGroupDescriptor desc = new BindGroupDescriptor {
			Layout = texBindGroupLayout,
			EntryCount = 2,
			Entries = entries
		};

		BindGroup *bg = Check(webgpu.DeviceCreateBindGroup(device, &desc));
		return new GPUBindGroup(this, bg);
	}

	// ==========================================================================
	// global state update
	private void queryAndResize(bool updateProj = true) {
		(uint w, uint h) = surfaceSource.GetDrawableSize();
		surfaceConfig = getSurfaceConfig(w, h);
		fixed (SurfaceConfiguration *cfg = &surfaceConfig)
			webgpu.SurfaceConfigure(surface, cfg);
		if (updateProj)
			updateProjection(w, h);
		Width = w;
		Height = h;
	}

	private void updateProjection(uint w, uint h) {
		GlobalsUniform @params = new GlobalsUniform {
			Projection = MatrixUtil.OrthoTopLeft(w, h)
		};
		WriteToBuffer(globalsUniformBuffer, 0, @params);
	}

	// ==========================================================================
	// public api

	/// <summary>
	/// Re-queries the drawable size and updates surface configuration /
	/// projection matrix state.
	/// </summary>
	public void Resized() {
		ObjectDisposedException.ThrowIf(disposed, this);
		queryAndResize();
	}

	/// <summary>
	/// Attempts to begin a new render frame.
	/// </summary>
	/// <param name="frame">On success, the newly created frame.</param>
	/// <returns>
	/// <see langword="true"/> if a frame was created successfully;
	/// <see langword="false"/> if no current surface texture could be acquired
	/// for this frame and rendering should be skipped.
	/// </returns>
	/// <remarks>
	/// Fatal failures throw exceptions instead of returning <see langword="false"/>.
	/// Recovery for outdated/lost surfaces is handled internally. Lost device /
	/// OOM is treated as fatal.
	/// </remarks>
	public bool TryBeginFrame([NotNullWhen(true)] out RenderFrame? frame) {
		ObjectDisposedException.ThrowIf(disposed, this);
		frame = null;
		if (!tryGetCurrentTex(out SurfaceTexture currTex))
			return false;

		TextureViewDescriptor tvdesc = new TextureViewDescriptor {
			Format = surfaceFormat,
			Dimension = TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = 1,
			BaseArrayLayer = 0,
			ArrayLayerCount = 1,
			Aspect = TextureAspect.All
		};
		TextureView *backbufferView = webgpu.TextureCreateView(currTex.Texture, &tvdesc);
		if (backbufferView == null) {
			webgpu.TextureRelease(currTex.Texture);
			throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
		}
		CommandEncoderDescriptor encDesc = default;
		CommandEncoder *enc = webgpu.DeviceCreateCommandEncoder(device, &encDesc);
		if (enc is null) {
			webgpu.TextureViewRelease(backbufferView);
			webgpu.TextureRelease(currTex.Texture);
			throw new WebGPUException("DeviceCreateCommandEncoder", "WebGPU call returned null");
		}
		frame = new RenderFrame(this, currTex, backbufferView, enc);
		return true;
	}

	/// <summary>
	/// Creates a GPU buffer, returning an owning object.
	/// </summary>
	/// <param name="size">Buffer size in bytes.</param>
	/// <param name="usage">Declared WebGPU usage flags for the buffer.</param>
	/// <param name="mappedAtCreation">
	/// Whether the buffer should be mapped immediately on creation.
	/// </param>
	public GPUBuffer CreateBuffer(ulong size, BufferUsage usage, bool mappedAtCreation = false) {
		ObjectDisposedException.ThrowIf(disposed, this);
		BufferDescriptor desc = new BufferDescriptor {
			Size = size,
			Usage = usage,
			MappedAtCreation = mappedAtCreation
		};
		Buffer *buffer = Check(webgpu.DeviceCreateBuffer(device, &desc));
		return new GPUBuffer(this, buffer, size, usage);
	}

	/// <summary>
	/// Writes a single unmanaged value into a GPU buffer using the renderer's queue.
	/// </summary>
	/// <typeparam name="T">Unmanaged value type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="val">Value to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBuffer buffer, ulong offset, in T val) where T : unmanaged {
		ObjectDisposedException.ThrowIf(disposed, this);
		fixed (T *p = &val)
			webgpu.QueueWriteBuffer(queue, buffer.Buffer, offset, p, (nuint)sizeof(T));
	}

	/// <summary>
	/// Writes data from a span into a GPU buffer using the renderer's queue.
	/// </summary>
	/// <typeparam name="T">Unmanaged element type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Data to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write. Empty spans are accepted and
	/// are a no-op.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBuffer buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (data.IsEmpty)
			return;
		fixed (T *p = data)
			webgpu.QueueWriteBuffer(queue, buffer.Buffer, offset, p, (nuint)(data.Length * sizeof(T)));
	}

	/// <summary>
	/// Writes data from a pointer into a GPU buffer using the renderer's queue.
	/// </summary>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Pointer to the data to upload.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write. The pointer only needs to remain
	/// valid for the duration of the call.
	/// </remarks>
	public void WriteToBuffer(GPUBuffer buffer, ulong offset, void *data, nuint size) {
		ObjectDisposedException.ThrowIf(disposed, this);
		webgpu.QueueWriteBuffer(queue, buffer.Buffer, offset, data, size);
	}

	/// <summary>
	/// Creates a texture and its default view, returning an owning object.
	/// </summary>
	/// <param name="params">Texture creation parameters.</param>
	public GPUTexture CreateTexture(in GPUTextureCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);
		TextureDescriptor desc = new TextureDescriptor {
			Dimension = TextureDimension.Dimension2D,
			Size = new Extent3D {
				Width = @params.Width,
				Height = @params.Height,
				DepthOrArrayLayers = @params.ArrayLayerCount
			},
			Format = @params.Format,
			MipLevelCount = @params.MipLevelCount,
			SampleCount = @params.SampleCount,
			Usage = @params.Usage
		};
		Texture *tex = Check(webgpu.DeviceCreateTexture(device, &desc));
		TextureViewDescriptor viewDesc = new TextureViewDescriptor {
			Format = @params.Format,
			Dimension = @params.ArrayLayerCount > 1 ? TextureViewDimension.Dimension2DArray : TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = @params.MipLevelCount,
			BaseArrayLayer = 0,
			ArrayLayerCount = @params.ArrayLayerCount,
			Aspect = TextureAspect.All
		};
		TextureView *view = webgpu.TextureCreateView(tex, &viewDesc);
		if (view is null) {
			webgpu.TextureRelease(tex);
			throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
		}
		return new GPUTexture(this, tex, view, @params.Width, @params.Height, @params.Format, @params.Usage, @params.MipLevelCount,
			@params.SampleCount, @params.ArrayLayerCount);
	}

	/// <summary>
	/// Writes texel data into a texture using the renderer's queue.
	/// </summary>
	/// <typeparam name="T">Unmanaged source element type.</typeparam>
	/// <param name="tex">Destination texture.</param>
	/// <param name="dst">Destination texture region and subresource.</param>
	/// <param name="data">Source texel data.</param>
	/// <param name="layout">Source memory layout.</param>
	/// <remarks>
	/// Empty spans are accepted and are a no-op.
	/// </remarks>
	public void WriteToTexture<T>(GPUTexture tex, in GPUTextureRegion dst, ReadOnlySpan<T> data, in GPUTextureLayout layout) where T : unmanaged {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (data.IsEmpty)
			return;
		fixed (T *p = data)
			WriteToTexture(tex, dst, p, (nuint)(data.Length * sizeof(T)), layout);
	}

	/// <summary>
	/// Writes texel data into a texture using the renderer's queue.
	/// </summary>
	/// <param name="tex">Destination texture.</param>
	/// <param name="dst">Destination texture region and subresource.</param>
	/// <param name="data">Pointer to the source texel data.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <param name="layout">Source memory layout.</param>
	/// <remarks>
	/// The pointer only needs to remain valid for the duration of the call.
	/// </remarks>
	public void WriteToTexture(GPUTexture tex, in GPUTextureRegion dst, void *data, nuint size, in GPUTextureLayout layout) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ImageCopyTexture copyDst = new ImageCopyTexture {
			Texture = tex.Texture,
			MipLevel = dst.MipLevel,
			Origin = new Origin3D {
				X = dst.X,
				Y = dst.Y,
				Z = dst.Z
			},
			Aspect = dst.Aspect
		};
		TextureDataLayout dataLayout = new TextureDataLayout {
			Offset = layout.Offset,
			BytesPerRow = layout.BytesPerRow,
			RowsPerImage = layout.RowsPerImage
		};
		Extent3D texSize = new Extent3D {
			Width = dst.Width,
			Height = dst.Height,
			DepthOrArrayLayers = dst.DepthOrArrayLayers
		};
		webgpu.QueueWriteTexture(queue, &copyDst, data, size, &dataLayout, &texSize);
	}

	/// <summary>
	/// Creates a sampler, returning an owning object.
	/// </summary>
	/// <param name="params">Sampler creation parameters.</param>
	public GPUSampler CreateSampler(in GPUSamplerCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);
		SamplerDescriptor desc = new SamplerDescriptor {
			MinFilter = @params.MinFilter,
			MagFilter = @params.MagFilter,
			MipmapFilter = @params.MipmapFilter,
			AddressModeU = @params.AddressModeU,
			AddressModeV = @params.AddressModeV,
			AddressModeW = @params.AddressModeW,
			MaxAnisotropy = 1
		};
		Sampler *sampler = Check(webgpu.DeviceCreateSampler(device, &desc));
		return new GPUSampler(this, sampler);
	}

	// TODO this needs to be changed to a more comprehensive api
	// and also needs a doc comment
	public GPUBindGroupLayout CreateSimpleBufferBindGroupLayout(ShaderStage visibility, ulong minBindingSize) {
		BindGroupLayoutEntry *bglEntries = stackalloc BindGroupLayoutEntry[1];
		bglEntries[0] = new BindGroupLayoutEntry {
			Binding = 0,
			Visibility = visibility,
			Buffer = new BufferBindingLayout {
				Type = BufferBindingType.Uniform,
				HasDynamicOffset = false,
				MinBindingSize = minBindingSize
			}
		};
		BindGroupLayoutDescriptor bglDesc = new BindGroupLayoutDescriptor {
			EntryCount = 1,
			Entries = bglEntries
		};
		return new GPUBindGroupLayout(this, Check(webgpu.DeviceCreateBindGroupLayout(device, &bglDesc)));
	}

	/// <summary>
	/// Creates a bind group that binds a range of a buffer to a single buffer
	/// binding in the given layout, returning an owning object.
	/// </summary>
	/// <param name="layout">
	/// Bind group layout whose buffer binding is being satisfied.
	/// </param>
	/// <param name="binding">
	/// Binding index within <paramref name="layout"/> to bind the buffer range to.
	/// </param>
	/// <param name="buffer">
	/// Buffer to be exposed through the binding.
	/// </param>
	/// <param name="offset">
	/// Byte offset into <paramref name="buffer"/> where the exposed range begins.
	/// </param>
	/// <param name="size">
	/// Size in bytes of the exposed range starting at <paramref name="offset"/>.
	/// </param>
	/// <remarks>
	/// Typically used for uniform-buffer bindings.
	/// The binding type in <paramref name="layout"/> must be compatible with
	/// the intended use of the buffer range.
	/// </remarks>
	public GPUBindGroup CreateBufferBindGroup(GPUBindGroupLayoutHandle layout, uint binding, GPUBuffer buffer, ulong offset, ulong size) {
		ObjectDisposedException.ThrowIf(disposed, this);
		BindGroupEntry *entries = stackalloc BindGroupEntry[1];
		entries[0] = new BindGroupEntry {
			Binding = binding,
			Buffer = buffer.Buffer,
			Offset = offset,
			Size = size
		};
		BindGroupDescriptor desc = new BindGroupDescriptor {
			Layout = layout.BindGroupLayout,
			EntryCount = 1,
			Entries = entries
		};
		BindGroup *bg = Check(webgpu.DeviceCreateBindGroup(device, &desc));
		return new GPUBindGroup(this, bg);
	}

	/// <summary>
	/// Creates a texture+sampler bind group, returning an owning object.
	/// </summary>
	/// <param name="texture">Texture whose default view will be sampled.</param>
	/// <param name="sampler">Sampler to pair with the texture view.</param>
	/// <remarks>
	/// The returned bind group matches <see cref="TextureBindGroupLayout"/>.
	/// </remarks>
	public GPUBindGroup CreateTextureBindGroup(GPUTexture texture, GPUSampler sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		return createTexViewPlusSamplerBindGroup(texture.View, sampler.Sampler);
	}

	/// <summary>
	/// Creates a texture+sampler bind group, returning an owning object.
	/// </summary>
	/// <param name="rt">Render target whose color view will be sampled.</param>
	/// <param name="sampler">Sampler to pair with the render target's color view.</param>
	/// <remarks>
	/// The returned bind group matches <see cref="TextureBindGroupLayout"/>.
	/// This does not bind the render target's depth attachment.
	/// </remarks>
	public GPUBindGroup CreateTextureBindGroup(GPURenderTarget rt, GPUSampler sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		return createTexViewPlusSamplerBindGroup(rt.ColorView, sampler.Sampler);
	}

	/// <summary>
	/// Creates an offscreen render target, returning an owning object.
	/// </summary>
	/// <param name="params">Render target creation parameters.</param>
	/// <remarks>
	/// The created color texture is configured for both render attachment use
	/// and texture sampling so that the render target can later be sampled via
	/// <see cref="CreateTextureBindGroup(GPURenderTarget, GPUSampler)"/> and drawn.
	/// </remarks>
	public GPURenderTarget CreateRenderTarget(in GPURenderTargetCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);
		TextureDescriptor colorDesc = new TextureDescriptor {
			Dimension = TextureDimension.Dimension2D,
			Size = new Extent3D {
				Width = @params.Width,
				Height = @params.Height,
				DepthOrArrayLayers = 1
			},
			Format = @params.Format,
			MipLevelCount = 1,
			SampleCount = 1,
			Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding
		};
		Texture *colorTex = Check(webgpu.DeviceCreateTexture(device, &colorDesc));
		TextureViewDescriptor colorViewDesc = new TextureViewDescriptor {
			Format = @params.Format,
			Dimension = TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = 1,
			BaseArrayLayer = 0,
			ArrayLayerCount = 1,
			Aspect = TextureAspect.All
		};
		TextureView *colorView = webgpu.TextureCreateView(colorTex, &colorViewDesc);
		if (colorView is null) {
			webgpu.TextureRelease(colorTex);
			throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
		}

		Texture *depthTex = null;
		TextureView *depthView = null;
		if (@params.HasDepthStencil) {
			const TextureFormat fmt = TextureFormat.Depth24Plus;

			TextureDescriptor depthDesc = new TextureDescriptor {
				Dimension = TextureDimension.Dimension2D,
				Size = new Extent3D {
					Width = @params.Width,
					Height = @params.Height,
					DepthOrArrayLayers = 1
				},
				Format = fmt,
				MipLevelCount = 1,
				SampleCount = 1,
				Usage = TextureUsage.RenderAttachment
			};
			depthTex = webgpu.DeviceCreateTexture(device, &depthDesc);
			if (depthTex is null) {
				webgpu.TextureViewRelease(colorView);
				webgpu.TextureRelease(colorTex);
				throw new WebGPUException("DeviceCreateTexture", "WebGPU call returned null");
			}
			TextureViewDescriptor depthViewDesc = new TextureViewDescriptor {
				Format = fmt,
				Dimension = TextureViewDimension.Dimension2D,
				BaseMipLevel = 0,
				MipLevelCount = 1,
				BaseArrayLayer = 0,
				ArrayLayerCount = 1,
				Aspect = TextureAspect.DepthOnly
			};
			depthView = webgpu.TextureCreateView(depthTex, &depthViewDesc);
			if (depthView is null) {
				webgpu.TextureRelease(depthTex);
				webgpu.TextureViewRelease(colorView);
				webgpu.TextureRelease(colorTex);
				throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
			}
		}
		return new GPURenderTarget(this, colorTex, colorView, depthTex, depthView, @params.Width, @params.Height, @params.Format);
	}

	/// <summary>
	/// Creates a shader module from WGSL source code, returning an owning object.
	/// </summary>
	/// <param name="code">WGSL source code.</param>
	public GPUShader CreateShaderWGSL(string code) {
		ObjectDisposedException.ThrowIf(disposed, this);
		byte *p = (byte *)SilkMarshal.StringToPtr(code, NativeStringEncoding.UTF8);
		try {
			ShaderModuleWGSLDescriptor src = new ShaderModuleWGSLDescriptor {
				Chain = new ChainedStruct {
					SType = SType.ShaderModuleWgslDescriptor,
					Next = null
				},
				Code = p
			};
			ShaderModuleDescriptor desc = new ShaderModuleDescriptor {
				NextInChain = (ChainedStruct *)&src
			};
			ShaderModule *module = Check(webgpu.DeviceCreateShaderModule(device, &desc));
			return new GPUShader(this, module);
		} finally {
			SilkMarshal.Free((IntPtr)p);
		}
	}

	/// <summary>
	/// Creates a pipeline layout from the given bind group layouts, returning an owning object.
	/// </summary>
	/// <param name="layouts">Bind group layouts in bind group order.</param>
	/// <remarks>
	/// The order of <paramref name="layouts"/> determines the bind group indices
	/// expected by pipelines created from the returned layout.
	/// </remarks>
	/// <exception cref="ArgumentException">Thrown if no layouts are provided.</exception>
	public GPUPipelineLayout CreatePipelineLayout(ReadOnlySpan<GPUBindGroupLayoutHandle> layouts) {
		ObjectDisposedException.ThrowIf(disposed, this);

		if (layouts.IsEmpty)
			throw new ArgumentException("pipeline layout must contain at least one bind group layout");

		BindGroupLayout **bgLayouts = stackalloc BindGroupLayout *[layouts.Length];
		for (int i = 0; i < layouts.Length; i++) {
			ArgumentNullException.ThrowIfNull(layouts[i]);
			bgLayouts[i] = layouts[i].BindGroupLayout;
		}

		PipelineLayoutDescriptor desc = new PipelineLayoutDescriptor {
			BindGroupLayoutCount = (nuint)layouts.Length,
			BindGroupLayouts = bgLayouts
		};
		PipelineLayout *layout = Check(webgpu.DeviceCreatePipelineLayout(device, &desc));
		return new GPUPipelineLayout(this, layout);
	}

	/// <summary>
	/// Creates a render pipeline, returning an owning object.
	/// </summary>
	/// <param name="layout">Pipeline layout, describing the expected bind groups.</param>
	/// <param name="params">Render pipeline creation parameters.</param>
	/// <remarks>
	/// Pipelines are color-target-format-specific. Code rendering to multiple
	/// target formats typically needs a separate pipeline per format.
	/// </remarks>
	public GPURenderPipeline CreateRenderPipeline(GPUPipelineLayout layout, in GPURenderPipelineCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);

		VertexAttribute *attrs = stackalloc VertexAttribute[@params.VertexAttributes.Length];
		@params.VertexAttributes.AsSpan().CopyTo(new Span<VertexAttribute>(attrs, @params.VertexAttributes.Length));

		VertexBufferLayout *vbuffers = stackalloc VertexBufferLayout[1];
		vbuffers[0] = new VertexBufferLayout {
			ArrayStride = @params.VertexStride,
			StepMode = @params.VertexStepMode,
			AttributeCount = (uint)@params.VertexAttributes.Length,
			Attributes = attrs
		};

		byte *vsEntry = (byte *)SilkMarshal.StringToPtr(@params.VertShaderEntryPoint, NativeStringEncoding.UTF8);
		byte *fsEntry = (byte *)SilkMarshal.StringToPtr(@params.FragShaderEntryPoint, NativeStringEncoding.UTF8);
		try {
			VertexState vert = new VertexState {
				Module = @params.Shader.ShaderModule,
				EntryPoint = vsEntry,
				BufferCount = 1,
				Buffers = vbuffers
			};

			BlendState blend = default;
			BlendState *blendp = null;
			if (@params.Blend is BlendState b) {
				blend = b;
				blendp = &blend;
			}

			ColorTargetState *targets = stackalloc ColorTargetState[1];
			targets[0] = new ColorTargetState {
				Format = @params.ColorTargetFormat,
				WriteMask = ColorWriteMask.All,
				Blend = blendp
			};

			FragmentState frag = new FragmentState {
				Module = @params.Shader.ShaderModule,
				EntryPoint = fsEntry,
				TargetCount = 1,
				Targets = targets
			};

			PrimitiveState primitive = new PrimitiveState {
				Topology = @params.PrimitiveTopology,
				FrontFace = @params.FrontFace,
				CullMode = @params.CullMode
			};

			DepthStencilState depthStencil = default;
			DepthStencilState *depthStencilP = null;
			if (@params.DepthStencil is DepthStencilState d) {
				depthStencil = d;
				depthStencilP = &depthStencil;
			}

			MultisampleState multisample = new MultisampleState {
				Count = 1,
				Mask = uint.MaxValue
			};

			RenderPipelineDescriptor desc = new RenderPipelineDescriptor {
				Layout = layout.PipelineLayout,
				Vertex = vert,
				Fragment = &frag,
				Primitive = primitive,
				DepthStencil = depthStencilP,
				Multisample = multisample
			};

			RenderPipeline *pipeline = Check(webgpu.DeviceCreateRenderPipeline(device, &desc));
			return new GPURenderPipeline(this, pipeline);
		} finally {
			SilkMarshal.Free((IntPtr)fsEntry);
			SilkMarshal.Free((IntPtr)vsEntry);
		}
	}

	/// <summary>
	/// Submits a finished command buffer to the renderer's queue.
	/// </summary>
	/// <remarks>
	/// Renderer-internal submission interface used by <see cref="RenderFrame"/>.
	/// Callers are expected to have finished all encoding / passes before this point.
	/// </remarks>
	internal void Submit(CommandBuffer *cmdbuf) {
		ObjectDisposedException.ThrowIf(disposed, this);
		webgpu.QueueSubmit(queue, 1, &cmdbuf);
	}

	/// <summary>
	/// Presents the currently acquired backbuffer surface texture.
	/// </summary>
	/// <remarks>
	/// Renderer-internal present step used by <see cref="RenderFrame.SubmitAndPresent"/>.
	/// This should only be called for a successfully begun frame that acquired a
	/// presentable surface texture.
	/// </remarks>
	internal void Present() {
		ObjectDisposedException.ThrowIf(disposed, this);
		webgpu.SurfacePresent(surface);
	}

	/// <summary>
	/// Releases all renderer-owned GPU state and invalidates all objects created from this renderer.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;

		if (texBindGroupLayout is not null) webgpu.BindGroupLayoutRelease(texBindGroupLayout);
		if (globalsUniformBindGroup is not null) webgpu.BindGroupRelease(globalsUniformBindGroup);
		if (globalsUniformBindGroupLayout is not null) webgpu.BindGroupLayoutRelease(globalsUniformBindGroupLayout);
		globalsUniformBuffer?.Dispose();
		if (queue is not null) webgpu.QueueRelease(queue);
		if (device is not null) webgpu.DeviceRelease(device);
		if (adapter is not null) webgpu.AdapterRelease(adapter);
		if (surface is not null) webgpu.SurfaceRelease(surface);
		if (instance is not null) webgpu.InstanceRelease(instance);
	}
}
