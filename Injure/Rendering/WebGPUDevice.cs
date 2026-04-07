// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

using Buffer = Silk.NET.WebGPU.Buffer;
using Surface = Silk.NET.WebGPU.Surface;
using Texture = Silk.NET.WebGPU.Texture;

using static Injure.Rendering.WebGPUException;

namespace Injure.Rendering;

/// <summary>
/// Owner of output-independent WebGPU state (instance, adapter, device, queue) and
/// resource creator/uploader.
/// Most other rendering objects are created from this type and should be
/// treated as invalid once it is disposed.
/// </summary>
public sealed unsafe class WebGPUDevice : IDisposable {
	// ==========================================================================
	// internal types
	private sealed class Request<TStatus, TObject> where TStatus : unmanaged, Enum where TObject : unmanaged {
		public int Done;
		public TStatus Status;
		public TObject *Object;
		public string Message = "<no message available>";
	}

	// ==========================================================================
	// internal objects / properties
	internal readonly WebGPU API;

	internal readonly Instance *Instance;
	internal readonly Adapter *Adapter;
	internal readonly Device *Device;
	internal readonly Queue *Queue;

	private readonly BindGroupLayout *globalsUniformBindGroupLayout;
	private readonly BindGroupLayout *texBindGroupLayout;

	private readonly GPUBindGroupLayoutRef globalsUniformBindGroupLayoutWrap;
	private readonly GPUBindGroupLayoutRef textureBindGroupLayoutWrap;

	private bool disposed = false;

	// ==========================================================================
	// public properties and ctor

	/// <summary>
	/// Ref to the global bind group layout for the globals uniform.
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
	/// Creates a <see cref="WebGPUDevice"/>.
	/// </summary>
	/// <param name="compatibleSurface"><c>compatibleSurface</c> to pass to adapter creation.</param>
	public WebGPUDevice(Surface *compatibleSurface = null) {
		API = WebGPU.GetApi();
		InstanceDescriptor instDesc = default;
		Instance = Check(API.CreateInstance(&instDesc));
		Adapter = requestAdapterBlocking(Instance, compatibleSurface);
		Device = requestDeviceBlocking(Instance, Adapter);
		Queue = Check(API.DeviceGetQueue(Device));
		globalsUniformBindGroupLayout = mkGlobalsUniformBindGroupLayout();
		globalsUniformBindGroupLayoutWrap = new GPUBindGroupLayoutRef(globalsUniformBindGroupLayout);
		texBindGroupLayout = mkTexBindGroupLayout();
		textureBindGroupLayoutWrap = new GPUBindGroupLayoutRef(texBindGroupLayout);
	}

	// ==========================================================================
	// resource creation
	private void waitRequest(Instance *instance, ref int done, string opName, int timeoutMs = 10000) {
		SpinWait sw = new SpinWait();
		long start = Environment.TickCount64;
		while (Volatile.Read(ref done) == 0) {
			API.InstanceProcessEvents(instance);
			if (Environment.TickCount64 - start > timeoutMs)
				throw new WebGPUException(opName, $"waiting for callback timed out (waited {timeoutMs} ms)");
			if (sw.NextSpinWillYield)
				Thread.Sleep(1);
			else
				sw.SpinOnce();
		}
	}

	private Adapter *requestAdapterBlocking(Instance *instance, Surface *compatibleSurface) {
		Request<RequestAdapterStatus, Adapter> req = new Request<RequestAdapterStatus, Adapter>();
		GCHandle h = GCHandle.Alloc(req);
		try {
			RequestAdapterOptions opts = default;
			opts.CompatibleSurface = compatibleSurface;
			opts.PowerPreference = PowerPreference.HighPerformance;

			PfnRequestAdapterCallback cb =
				(delegate *unmanaged[Cdecl] <RequestAdapterStatus, Adapter *, byte *, void *, void>)&adapterRequestedCallback;
			API.InstanceRequestAdapter(instance, &opts, cb, (void *)GCHandle.ToIntPtr(h));
			waitRequest(instance, ref req.Done, "InstanceRequestAdapter");
			if (req.Status != RequestAdapterStatus.Success || req.Object is null)
				throw new WebGPUException("InstanceRequestAdapter", req.Message);
			return req.Object;
		} finally {
			h.Free();
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void adapterRequestedCallback(RequestAdapterStatus status, Adapter *adapter, byte *message, void *userdata) {
		GCHandle h = GCHandle.FromIntPtr((IntPtr)userdata);
		Request<RequestAdapterStatus, Adapter> req = (Request<RequestAdapterStatus, Adapter>)h.Target!;
		req.Status = status;
		req.Object = adapter;
		req.Message = (message is not null ? SilkMarshal.PtrToString((IntPtr)message) : null) ?? "<no message available>";
		Volatile.Write(ref req.Done, 1);
	}

	private Device *requestDeviceBlocking(Instance *instance, Adapter *adapter) {
		Request<RequestDeviceStatus, Device> req = new Request<RequestDeviceStatus, Device>();
		GCHandle h = GCHandle.Alloc(req);
		try {
			DeviceDescriptor desc = default;
			PfnRequestDeviceCallback cb =
				(delegate *unmanaged[Cdecl] <RequestDeviceStatus, Device *, byte *, void *, void>)&deviceRequestedCallback;
			API.AdapterRequestDevice(adapter, &desc, cb, (void *)GCHandle.ToIntPtr(h));
			waitRequest(instance, ref req.Done, "AdapterRequestDevice");
			if (req.Status != RequestDeviceStatus.Success || req.Object is null)
				throw new WebGPUException("AdapterRequestDevice", req.Message);
			return req.Object;
		} finally {
			h.Free();
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void deviceRequestedCallback(RequestDeviceStatus status, Device *device, byte *message, void *userdata) {
		GCHandle h = GCHandle.FromIntPtr((IntPtr)userdata);
		Request<RequestDeviceStatus, Device> req = (Request<RequestDeviceStatus, Device>)h.Target!;
		req.Status = status;
		req.Object = device;
		req.Message = (message is not null ? SilkMarshal.PtrToString((IntPtr)message) : null) ?? "<no message available>";
		Volatile.Write(ref req.Done, 1);
	}

	private BindGroupLayout *mkGlobalsUniformBindGroupLayout() {
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
		return Check(API.DeviceCreateBindGroupLayout(Device, &bglDesc));
	}

	private BindGroupLayout *mkTexBindGroupLayout() {
		BindGroupLayoutEntry *bglEntries = stackalloc BindGroupLayoutEntry[2];
		bglEntries[0] = new BindGroupLayoutEntry {
			Binding = 0,
			Visibility = ShaderStage.Fragment,
			Texture = new TextureBindingLayout {
				SampleType = TextureSampleType.Float,
				ViewDimension = TextureViewDimension.Dimension2D, // TODO: support for texture arrays
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
		return Check(API.DeviceCreateBindGroupLayout(Device, &bglDesc));
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

		BindGroup *bg = Check(API.DeviceCreateBindGroup(Device, &desc));
		return new GPUBindGroup(this, bg);
	}

	// ==========================================================================
	// public api

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
		Buffer *buffer = Check(API.DeviceCreateBuffer(Device, &desc));
		return new GPUBuffer(this, buffer, size, usage);
	}

	/// <summary>
	/// Writes a single unmanaged value into a GPU buffer using the queue.
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
			API.QueueWriteBuffer(Queue, buffer.Buffer, offset, p, (nuint)sizeof(T));
	}

	/// <summary>
	/// Writes data from a span into a GPU buffer using the queue.
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
			API.QueueWriteBuffer(Queue, buffer.Buffer, offset, p, (nuint)(data.Length * sizeof(T)));
	}

	/// <summary>
	/// Writes data from a pointer into a GPU buffer using the queue.
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
		API.QueueWriteBuffer(Queue, buffer.Buffer, offset, data, size);
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
		Texture *tex = Check(API.DeviceCreateTexture(Device, &desc));
		TextureViewDescriptor viewDesc = new TextureViewDescriptor {
			Format = @params.Format,
			Dimension = @params.ArrayLayerCount > 1 ? TextureViewDimension.Dimension2DArray : TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = @params.MipLevelCount,
			BaseArrayLayer = 0,
			ArrayLayerCount = @params.ArrayLayerCount,
			Aspect = TextureAspect.All
		};
		TextureView *view = API.TextureCreateView(tex, &viewDesc);
		if (view is null) {
			API.TextureRelease(tex);
			throw new WebGPUException("TextureCreateView", "WebGPU call returned null");
		}
		return new GPUTexture(this, tex, view, @params.Width, @params.Height, @params.Format, @params.Usage, @params.MipLevelCount,
			@params.SampleCount, @params.ArrayLayerCount);
	}

	/// <summary>
	/// Writes texel data into a texture using the queue.
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
	/// Writes texel data into a texture using the queue.
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
		API.QueueWriteTexture(Queue, &copyDst, data, size, &dataLayout, &texSize);
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
		Sampler *sampler = Check(API.DeviceCreateSampler(Device, &desc));
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
		return new GPUBindGroupLayout(this, Check(API.DeviceCreateBindGroupLayout(Device, &bglDesc)));
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
		BindGroup *bg = Check(API.DeviceCreateBindGroup(Device, &desc));
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
		Texture *colorTex = Check(API.DeviceCreateTexture(Device, &colorDesc));
		TextureViewDescriptor colorViewDesc = new TextureViewDescriptor {
			Format = @params.Format,
			Dimension = TextureViewDimension.Dimension2D,
			BaseMipLevel = 0,
			MipLevelCount = 1,
			BaseArrayLayer = 0,
			ArrayLayerCount = 1,
			Aspect = TextureAspect.All
		};
		TextureView *colorView = API.TextureCreateView(colorTex, &colorViewDesc);
		if (colorView is null) {
			API.TextureRelease(colorTex);
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
			depthTex = API.DeviceCreateTexture(Device, &depthDesc);
			if (depthTex is null) {
				API.TextureViewRelease(colorView);
				API.TextureRelease(colorTex);
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
			depthView = API.TextureCreateView(depthTex, &depthViewDesc);
			if (depthView is null) {
				API.TextureRelease(depthTex);
				API.TextureViewRelease(colorView);
				API.TextureRelease(colorTex);
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
			ShaderModule *module = Check(API.DeviceCreateShaderModule(Device, &desc));
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
		PipelineLayout *layout = Check(API.DeviceCreatePipelineLayout(Device, &desc));
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

			RenderPipeline *pipeline = Check(API.DeviceCreateRenderPipeline(Device, &desc));
			return new GPURenderPipeline(this, pipeline);
		} finally {
			SilkMarshal.Free((IntPtr)fsEntry);
			SilkMarshal.Free((IntPtr)vsEntry);
		}
	}

	/// <summary>
	/// Submits a finished command buffer to the queue.
	/// </summary>
	/// <remarks>
	/// Renderer-internal submission interface used by <see cref="RenderFrame"/>.
	/// Callers are expected to have finished all encoding / passes before this point.
	/// </remarks>
	internal void Submit(CommandBuffer *cmdbuf) {
		ObjectDisposedException.ThrowIf(disposed, this);
		API.QueueSubmit(Queue, 1, &cmdbuf);
	}

	/// <summary>
	/// Releases all owned GPU state and invalidates all objects created from this <see cref="WebGPUDevice"/>.
	/// </summary>
	public void Dispose() {
		if (disposed)
			return;
		disposed = true;

		if (texBindGroupLayout is not null) API.BindGroupLayoutRelease(texBindGroupLayout);
		if (globalsUniformBindGroupLayout is not null) API.BindGroupLayoutRelease(globalsUniformBindGroupLayout);
		if (Queue is not null) API.QueueRelease(Queue);
		if (Device is not null) API.DeviceRelease(Device);
		if (Adapter is not null) API.AdapterRelease(Adapter);
		if (Instance is not null) API.InstanceRelease(Instance);
		API.Dispose();
	}
}
