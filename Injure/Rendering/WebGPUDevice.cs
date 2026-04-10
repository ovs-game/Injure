// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
/// <para>
/// Owner of output-independent WebGPU state (instance, adapter, device, queue) and
/// resource creator/uploader.
/// Most other rendering objects are created from this type and should be
/// treated as invalid once it is disposed.
/// </para>
/// <para>
/// Also owns standard bind group layouts for common cases, and provides convenience
/// helpers to create matching bind groups.
/// </para>
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

	private readonly GPUBindGroupLayout globalsUniformBindGroupLayout;
	private readonly GPUBindGroupLayout colorTex2DBindGroupLayout;
	private readonly GPUBindGroupLayout filteringDepthTex2DBindGroupLayout;
	private readonly GPUBindGroupLayout comparisonDepthTex2DBindGroupLayout;

	private bool disposed = false;

	// ==========================================================================
	// public properties and ctor

	/// <summary>
	/// Standard bind group layout for the globals uniform, describing a single
	/// vertex-visible uniform buffer binding at binding 0 with a minimum binding
	/// size of <c>sizeof(GlobalsUniform)</c>.
	/// </summary>
	/// <remarks>
	/// Functionally equivalent to a bind group layout created with:
	/// <code>
	/// CreateBindGroupLayout([
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 0,
	/// 		Visibility: ShaderStage.Vertex,
	/// 		Layout: new GPUBufferBindingLayout(
	/// 			Type: BufferBindingType.Uniform,
	/// 			MinBindingSize: (ulong)sizeof(GlobalsUniform)
	/// 		)
	/// 	)
	/// ]);
	/// </code>
	/// </remarks>
	public GPUBindGroupLayoutRef StdGlobalsUniformLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
		}
	}

	/// <summary>
	/// Standard bind group layout for a 2D color texture + sampler, describing a
	/// <c>"float"</c>-sampled, 2D, non-multisampled color texture view at binding 0
	/// and a filtering sampler at binding 1.
	/// </summary>
	/// <remarks>
	/// Functionally equivalent to a bind group layout created with:
	/// <code>
	/// CreateBindGroupLayout([
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 0,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUTextureBindingLayout(
	/// 			SampleType: TextureSampleType.Float,
	/// 			ViewDimension: TextureViewDimension.Dimension2D,
	/// 			Multisampled: false
	/// 		)
	/// 	),
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 1,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUSamplerBindingLayout(
	/// 			Type: SamplerBindingType.Filtering
	/// 		)
	/// 	)
	/// ]);
	/// </code>
	/// </remarks>
	public GPUBindGroupLayoutRef StdColorTexture2DLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
		}
	}

	/// <summary>
	/// Standard bind group layout for a 2D depth texture + filtering sampler, describing a
	/// <c>"depth"</c>-sampled, 2D, non-multisampled depth-only texture view at binding 0
	/// and a filtering sampler at binding 1.
	/// </summary>
	/// <remarks>
	/// Functionally equivalent to a bind group layout created with:
	/// <code>
	/// CreateBindGroupLayout([
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 0,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUTextureBindingLayout(
	/// 			SampleType: TextureSampleType.Depth,
	/// 			ViewDimension: TextureViewDimension.Dimension2D,
	/// 			Multisampled: false
	/// 		)
	/// 	),
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 1,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUSamplerBindingLayout(
	/// 			Type: SamplerBindingType.Filtering
	/// 		)
	/// 	)
	/// ]);
	/// </code>
	/// </remarks>
	public GPUBindGroupLayoutRef StdFilteringDepthTexture2DLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
		}
	}

	/// <summary>
	/// Standard bind group layout for a 2D depth texture + comparison sampler, describing a
	/// <c>"depth"</c>-sampled, 2D, non-multisampled depth-only texture view at binding 0
	/// and a comparison sampler at binding 1.
	/// </summary>
	/// <remarks>
	/// Functionally equivalent to a bind group layout created with:
	/// <code>
	/// CreateBindGroupLayout([
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 0,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUTextureBindingLayout(
	/// 			SampleType: TextureSampleType.Depth,
	/// 			ViewDimension: TextureViewDimension.Dimension2D,
	/// 			Multisampled: false
	/// 		)
	/// 	),
	/// 	new GPUBindGroupLayoutEntry(
	/// 		Binding: 1,
	/// 		Visibility: ShaderStage.Fragment,
	/// 		Layout: new GPUSamplerBindingLayout(
	/// 			Type: SamplerBindingType.Comparison
	/// 		)
	/// 	)
	/// ]);
	/// </code>
	/// </remarks>
	public GPUBindGroupLayoutRef StdComparisonDepthTexture2DLayout {
		get {
			ObjectDisposedException.ThrowIf(disposed, this);
			return field;
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

		globalsUniformBindGroupLayout = CreateBindGroupLayout([
			new GPUBindGroupLayoutEntry(
				Binding: 0,
				Visibility: ShaderStage.Vertex,
				Layout: new GPUBufferBindingLayout(
					Type: BufferBindingType.Uniform,
					MinBindingSize: (ulong)sizeof(GlobalsUniform)
				)
			)
		]);
		StdGlobalsUniformLayout = globalsUniformBindGroupLayout.AsRef();

		colorTex2DBindGroupLayout = CreateBindGroupLayout([
			new GPUBindGroupLayoutEntry(
				Binding: 0,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUTextureBindingLayout(
					SampleType: TextureSampleType.Float,
					ViewDimension: TextureViewDimension.Dimension2D,
					Multisampled: false
				)
			),
			new GPUBindGroupLayoutEntry(
				Binding: 1,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUSamplerBindingLayout(
					Type: SamplerBindingType.Filtering
				)
			)
		]);
		StdColorTexture2DLayout = colorTex2DBindGroupLayout.AsRef();

		filteringDepthTex2DBindGroupLayout = CreateBindGroupLayout([
			new GPUBindGroupLayoutEntry(
				Binding: 0,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUTextureBindingLayout(
					SampleType: TextureSampleType.Depth,
					ViewDimension: TextureViewDimension.Dimension2D,
					Multisampled: false
				)
			),
			new GPUBindGroupLayoutEntry(
				Binding: 1,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUSamplerBindingLayout(
					Type: SamplerBindingType.Filtering
				)
			)
		]);
		StdFilteringDepthTexture2DLayout = filteringDepthTex2DBindGroupLayout.AsRef();

		comparisonDepthTex2DBindGroupLayout = CreateBindGroupLayout([
			new GPUBindGroupLayoutEntry(
				Binding: 0,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUTextureBindingLayout(
					SampleType: TextureSampleType.Depth,
					ViewDimension: TextureViewDimension.Dimension2D,
					Multisampled: false
				)
			),
			new GPUBindGroupLayoutEntry(
				Binding: 1,
				Visibility: ShaderStage.Fragment,
				Layout: new GPUSamplerBindingLayout(
					Type: SamplerBindingType.Comparison
				)
			)
		]);
		StdComparisonDepthTexture2DLayout = comparisonDepthTex2DBindGroupLayout.AsRef();
	}

	// ==========================================================================
	// resource acquisition
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

	// ==========================================================================
	// public api (core)

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
	/// Writes a single unmanaged value into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged value type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="val">Value to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBufferHandle buffer, ulong offset, in T val) where T : unmanaged {
		ObjectDisposedException.ThrowIf(disposed, this);
		fixed (T *p = &val)
			API.QueueWriteBuffer(Queue, buffer.Buffer, offset, p, (nuint)sizeof(T));
	}

	/// <summary>
	/// Writes data from a span into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged element type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Data to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write. Empty spans are accepted and
	/// are a no-op.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBufferHandle buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (data.IsEmpty)
			return;
		fixed (T *p = data)
			API.QueueWriteBuffer(Queue, buffer.Buffer, offset, p, (nuint)(data.Length * sizeof(T)));
	}

	/// <summary>
	/// Writes data from a pointer into a GPU buffer.
	/// </summary>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Pointer to the data to upload.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <remarks>
	/// This is a queue write, not a mapped buffer write. The pointer only needs to remain
	/// valid for the duration of the call.
	/// </remarks>
	public void WriteToBuffer(GPUBufferHandle buffer, ulong offset, void *data, nuint size) {
		ObjectDisposedException.ThrowIf(disposed, this);
		API.QueueWriteBuffer(Queue, buffer.Buffer, offset, data, size);
	}

	/// <summary>
	/// Creates a texture and its default view, returning an owning object.
	/// </summary>
	/// <param name="params">Texture creation parameters.</param>
	/// <exception cref="ArgumentException">
	/// Thrown if the provided <see cref="GPUTextureCreateParams.ViewFormats"/> contains
	/// the texture's <see cref="GPUTextureCreateParams.Format"/> or any duplicates.
	/// </exception>
	public GPUTexture CreateTexture(in GPUTextureCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);
		TextureFormat[] viewFormats = @params.ViewFormats.ToArray(); // intentionally copy out

		HashSet<TextureFormat> tmp = new HashSet<TextureFormat>();
		if (!viewFormats.All(tmp.Add))
			throw new ArgumentException("ViewFormats must not contain duplicates", nameof(@params));
		if (tmp.Contains(@params.Format))
			throw new ArgumentException("ViewFormats must not contain the texture's format", nameof(@params));

		fixed (TextureFormat *p = viewFormats) {
			TextureDescriptor desc = new TextureDescriptor {
				Size = new Extent3D {
					Width = @params.Width,
					Height = @params.Height,
					DepthOrArrayLayers = @params.DepthOrArrayLayers
				},
				MipLevelCount = @params.MipLevelCount,
				SampleCount = @params.SampleCount,
				Dimension = @params.Dimension,
				Format = @params.Format,
				Usage = @params.Usage,
				ViewFormatCount = (nuint)viewFormats.Length,
				ViewFormats = p
			};
			Texture *tex = Check(API.DeviceCreateTexture(Device, &desc));
			try {
				// TODO: think about whether default view creation should be external
				// so that the try-catch isn't necessary
				return new GPUTexture(this, tex, @params.Width, @params.Height, @params.DepthOrArrayLayers,
					@params.MipLevelCount, @params.SampleCount, @params.Dimension, @params.Format, @params.Usage,
					viewFormats);
			} catch (WebGPUException) {
				API.TextureRelease(tex);
				throw;
			}
		}
	}

	/// <summary>
	/// Writes texel data into a texture.
	/// </summary>
	/// <typeparam name="T">Unmanaged source element type.</typeparam>
	/// <param name="tex">Destination texture.</param>
	/// <param name="dst">Destination texture region and subresource.</param>
	/// <param name="data">Source texel data.</param>
	/// <param name="layout">Source memory layout.</param>
	/// <remarks>
	/// <para>
	/// The number of bytes consumed from each source row is determined by the
	/// uploaded texture region and format, while <see cref="GPUTextureLayout.BytesPerRow"/>
	/// is the spacing between row starts in memory. This allows uploading from data
	/// with padding between rows.
	/// </para>
	/// <para>
	/// There are no alignment/etc. restrictions on the values in <see cref="GPUTextureLayout"/>.
	/// </para>
	/// <para>
	/// Empty spans are accepted and are a no-op.
	/// </para>
	/// </remarks>
	public void WriteToTexture<T>(GPUTextureHandle tex, in GPUTextureRegion dst, ReadOnlySpan<T> data, in GPUTextureLayout layout) where T : unmanaged {
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
	/// <para>
	/// The number of bytes consumed from each source row is determined by the
	/// uploaded texture region and format, while <see cref="GPUTextureLayout.BytesPerRow"/>
	/// is the spacing between row starts in memory. This allows uploading from data
	/// with padding between rows.
	/// </para>
	/// <para>
	/// There are no alignment/etc. restrictions on the values in <see cref="GPUTextureLayout"/>.
	/// </para>
	/// <para>
	/// The pointer only needs to remain valid for the duration of the call.
	/// </para>
	/// </remarks>
	public void WriteToTexture(GPUTextureHandle tex, in GPUTextureRegion dst, void *data, nuint size, in GPUTextureLayout layout) {
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
	/// <exception cref="ArgumentException">
	/// Thrown if <see cref="GPUSamplerCreateParams.MaxAnisotropy"/> is larger than 1 and
	/// any of the filter modes is set to anything but linear filtering.
	/// </exception>
	public GPUSampler CreateSampler(in GPUSamplerCreateParams @params) {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (@params.LodMinClamp < 0)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMinClamp cannot be negative");
		if (@params.LodMaxClamp < @params.LodMinClamp)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMaxClamp cannot be smaller than LodMinClamp");
		if (@params.MaxAnisotropy < 1)
			throw new ArgumentOutOfRangeException(nameof(@params), "MaxAnisotropy must be at least 1");
		if (@params.MaxAnisotropy > 1 &&
			(@params.MinFilter != FilterMode.Linear || @params.MagFilter != FilterMode.Linear || @params.MipmapFilter != MipmapFilterMode.Linear))
			throw new ArgumentException("MinFilter/MagFilter/MipMapFilter must be set to Linear if MaxAnisotropy > 1", nameof(@params));
		SamplerDescriptor desc = new SamplerDescriptor {
			AddressModeU = @params.AddressModeU,
			AddressModeV = @params.AddressModeV,
			AddressModeW = @params.AddressModeW,
			MagFilter = @params.MagFilter,
			MinFilter = @params.MinFilter,
			MipmapFilter = @params.MipmapFilter,
			LodMinClamp = @params.LodMinClamp,
			LodMaxClamp = @params.LodMaxClamp,
			Compare = @params.Compare,
			MaxAnisotropy = @params.MaxAnisotropy

		};
		Sampler *sampler = Check(API.DeviceCreateSampler(Device, &desc));
		return new GPUSampler(this, sampler);
	}

	private static BindGroupLayoutEntry toRawBindGroupLayoutEntry(in GPUBindGroupLayoutEntry entry) {
		BindGroupLayoutEntry raw = new BindGroupLayoutEntry {
			Binding = entry.Binding,
			Visibility = entry.Visibility
		};
		switch (entry.Layout) {
		case GPUBufferBindingLayout b:
			raw.Buffer = new BufferBindingLayout {
				Type = b.Type,
				HasDynamicOffset = b.HasDynamicOffset,
				MinBindingSize = b.MinBindingSize
			};
			return raw;
		case GPUSamplerBindingLayout s:
			raw.Sampler = new SamplerBindingLayout {
				Type = s.Type
			};
			return raw;
		case GPUStorageTextureBindingLayout st:
			raw.StorageTexture = new StorageTextureBindingLayout {
				Access = st.Access,
				Format = st.Format,
				ViewDimension = st.ViewDimension
			};
			return raw;
		case GPUTextureBindingLayout t:
			raw.Texture = new TextureBindingLayout {
				SampleType = t.SampleType,
				ViewDimension = t.ViewDimension,
				Multisampled = t.Multisampled
			};
			return raw;
		default:
			throw new UnreachableException();
		}
	}

	/// <summary>
	/// Creates a bind group layout from the given entries, returning an owning object.
	/// </summary>
	/// <param name="entries">Entries describing the bindings in the layout.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if any entry has a <see langword="null"/> layout.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="entries"/> is empty, contains duplicate binding indices,
	/// or contains an unsupported entry layout type (e.g an unknown type that derives from
	/// <see cref="GPUBindingLayout"/>).
	/// </exception>
	public GPUBindGroupLayout CreateBindGroupLayout(ReadOnlySpan<GPUBindGroupLayoutEntry> entries) {
		ObjectDisposedException.ThrowIf(disposed, this);
		if (entries.IsEmpty)
			throw new ArgumentException("bind group layout must contain at least one entry", nameof(entries));

		for (int i = 0; i < entries.Length; i++) {
			ref readonly GPUBindGroupLayoutEntry e = ref entries[i];
			ArgumentNullException.ThrowIfNull(e.Layout);

			// O(n^2) on paper but it's honestly probably better than allocation/etc for
			// a hashset when there's probably gonna be like only a few entries 99% of the time
			for (int j = 0; j < i; j++)
				if (entries[j].Binding == e.Binding)
					throw new ArgumentException($"duplicate bind group layout binding {e.Binding}", nameof(entries));

			switch (e.Layout) {
			case GPUBufferBindingLayout:
			case GPUSamplerBindingLayout:
			case GPUTextureBindingLayout:
			case GPUStorageTextureBindingLayout:
				break;
			default:
				throw new ArgumentException($"unsupported bind group layout entry type {e.Layout.GetType().FullName}", nameof(entries));
			}
		}

		BindGroupLayoutEntry *rawEntries = stackalloc BindGroupLayoutEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupLayoutEntry(entries[i]);
		BindGroupLayoutDescriptor desc = new BindGroupLayoutDescriptor {
			EntryCount = (nuint)entries.Length,
			Entries = rawEntries
		};
		return new GPUBindGroupLayout(this, Check(API.DeviceCreateBindGroupLayout(Device, &desc)));
	}

	private static BindGroupEntry toRawBindGroupEntry(in GPUBindGroupEntry entry) {
		BindGroupEntry raw = new BindGroupEntry {
			Binding = entry.Binding
		};
		switch (entry.Resource) {
		case GPUBufferBindingResource b:
			raw.Buffer = b.Buffer.Buffer;
			raw.Offset = b.Offset;
			raw.Size = b.Size ?? (b.Buffer.Size - b.Offset);
			return raw;
		case GPUSamplerBindingResource s:
			raw.Sampler = s.Sampler.Sampler;
			return raw;
		case GPUTextureViewBindingResource v:
			raw.TextureView = v.View.TextureView;
			return raw;
		default:
			throw new UnreachableException();
		}
	}

	/// <summary>
	/// Creates a bind group from the given layout and entries, returning an owning object.
	/// </summary>
	/// <param name="layout">Bind group layout the created bind group must satisfy.</param>
	/// <param name="entries">Binding entries to populate in the bind group.</param>
	/// <remarks>
	/// The caller is responsible for providing entries compatible with
	/// <paramref name="layout"/>. This method catches obvious bugs such as
	/// duplicate bindings and invalid buffer ranges, but does not attempt to
	/// validate layout compatibility.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="layout"/> is <see langword="null"/> or if any
	/// entry contains a <see langword="null"/> resource.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="entries"/> is empty, contains duplicate binding indices,
	/// contains an unsupported entry resource type (e.g an unknown type that derives from
	/// <see cref="GPUBindingResource"/>), or contains an invalid/malformed entry resource.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown if a render target depth view was requested for a render target that has
	/// no depth attachment.
	/// </exception>
	public GPUBindGroup CreateBindGroup(GPUBindGroupLayoutHandle layout, ReadOnlySpan<GPUBindGroupEntry> entries) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(layout);
		if (entries.IsEmpty)
			throw new ArgumentException("bind group must contain at least one entry", nameof(entries));

		for (int i = 0; i < entries.Length; i++) {
			ref readonly GPUBindGroupEntry e = ref entries[i];
			ArgumentNullException.ThrowIfNull(e.Resource);

			// O(n^2) on paper but it's honestly probably better than allocation/etc for
			// a hashset when there's probably gonna be like only a few entries 99% of the time
			for (int j = 0; j < i; j++)
				if (entries[j].Binding == e.Binding)
					throw new ArgumentException($"duplicate bind group binding {e.Binding}", nameof(entries));

			switch (e.Resource) {
			case GPUBufferBindingResource b:
				ArgumentNullException.ThrowIfNull(b.Buffer);
				if (b.Offset > b.Buffer.Size)
					throw new ArgumentException($"buffer binding {e.Binding} has an offset past the end of the buffer", nameof(entries));
				ulong size = b.Size ?? (b.Buffer.Size - b.Offset);
				if (size == 0)
					throw new ArgumentException($"buffer binding {e.Binding} must expose a nonzero range", nameof(entries));
				if (b.Offset + size > b.Buffer.Size)
					throw new ArgumentException($"buffer binding {e.Binding} range extends past the end of the buffer", nameof(entries));
				break;
			case GPUSamplerBindingResource s:
				ArgumentNullException.ThrowIfNull(s.Sampler);
				break;
			case GPUTextureViewBindingResource v:
				ArgumentNullException.ThrowIfNull(v.View);
				break;
			default:
				throw new ArgumentException($"unsupported bind group entry resource type {e.Resource.GetType().FullName}", nameof(entries));
			}
		}

		BindGroupEntry *rawEntries = stackalloc BindGroupEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupEntry(entries[i]);
		BindGroupDescriptor desc = new BindGroupDescriptor {
			Layout = layout.BindGroupLayout,
			EntryCount = (nuint)entries.Length,
			Entries = rawEntries
		};
		return new GPUBindGroup(this, Check(API.DeviceCreateBindGroup(Device, &desc)));
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
				WriteMask = @params.ColorWriteMask,
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

		filteringDepthTex2DBindGroupLayout.Dispose();
		comparisonDepthTex2DBindGroupLayout.Dispose();
		colorTex2DBindGroupLayout.Dispose();
		globalsUniformBindGroupLayout.Dispose();
		API.QueueRelease(Queue);
		API.DeviceRelease(Device);
		API.AdapterRelease(Adapter);
		API.InstanceRelease(Instance);
		API.Dispose();
	}

	// ==========================================================================
	// public api (sugar/convenience)

	/// <summary>
	/// Creates a bind group layout with a single uniform buffer, returning
	/// an owning object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="CreateBindGroupLayout(ReadOnlySpan{GPUBindGroupLayoutEntry})"/>; see
	/// its docs for documentation.
	/// </para>
	/// <para>
	/// Intended to be used together with <see cref="CreateUniformBufferBindGroup(GPUBindGroupLayoutHandle, GPUBufferHandle, ulong, ulong?, uint)"/>
	/// for the simple common usecase of binding a single uniform with e.g shader parameters.
	/// </para>
	/// </remarks>
	public GPUBindGroupLayout CreateUniformBufferBindGroupLayout(ShaderStage visibility,
		ulong minBindingSize = 0, bool hasDynamicOffset = false, uint binding = 0) =>
		CreateBindGroupLayout([
			new GPUBindGroupLayoutEntry(
				Binding: binding,
				Visibility: visibility,
				Layout: new GPUBufferBindingLayout(
					Type: BufferBindingType.Uniform,
					HasDynamicOffset: hasDynamicOffset,
					MinBindingSize: minBindingSize
				)
			)
		]);

	/// <summary>
	/// Creates a bind group with a single uniform buffer, returning an
	/// owning object.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="CreateBindGroup(GPUBindGroupLayoutHandle, ReadOnlySpan{GPUBindGroupEntry})"/>; see
	/// its docs for documentation.
	/// </para>
	/// <para>
	/// Intended to be used together with <see cref="CreateUniformBufferBindGroupLayout(ShaderStage, ulong, bool, uint)"/>
	/// for the simple common usecase of binding a single uniform with e.g shader parameters.
	/// </para>
	/// </remarks>
	public GPUBindGroup CreateUniformBufferBindGroup(GPUBindGroupLayoutHandle layout,
		GPUBufferHandle buffer, ulong offset = 0, ulong? size = null, uint binding = 0) =>
		CreateBindGroup(layout, [
			new GPUBindGroupEntry(
				Binding: binding,
				Resource: new GPUBufferBindingResource(
					Buffer: buffer,
					Offset: offset,
					Size: size
				)
			)
		]);

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D color texture view and a
	/// filtering sampler, returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled color view to sample.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="CreateBindGroup(GPUBindGroupLayoutHandle, ReadOnlySpan{GPUBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="StdColorTexture2DLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"float"</c> or
	/// to check if the sampler is a filtering sampler is made, so if it isn't, a WebGPU
	/// validation error may happen.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, color view.
	/// </exception>
	public GPUBindGroup CreateStdColorTexture2DBindGroup(GPUTextureViewHandle view, GPUSamplerHandle sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if ((view.Usage & TextureUsage.TextureBinding) == 0)
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (view.Format is TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32float
			or TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32floatStencil8 or TextureFormat.Stencil8)
			throw new ArgumentException("view must be a color format", nameof(view));
		return CreateBindGroup(StdColorTexture2DLayout, [
			new GPUBindGroupEntry(Binding: 0, new GPUTextureViewBindingResource(view)),
			new GPUBindGroupEntry(Binding: 1, new GPUSamplerBindingResource(sampler))
		]);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a texture's default view, assuming
	/// it is a 2D color view, and a filtering sampler, returning an owning object.
	/// </summary>
	/// <param name="texture">Texture whose default view will be used.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="texture"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="texture"/>'s <see cref="GPUTextureHandle.DefaultView"/> is
	/// not a <see cref="TextureUsage.TextureBinding"/>-enabled, 2D, non-multisampled, color view.
	/// </exception>
	/// <inheritdoc cref="CreateStdColorTexture2DBindGroup(GPUTextureViewHandle, GPUSamplerHandle)"/>
	public GPUBindGroup CreateStdColorTexture2DBindGroup(GPUTextureHandle texture, GPUSamplerHandle sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(texture);
		return CreateStdColorTexture2DBindGroup(texture.DefaultView, sampler);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D depth-only texture view and a
	/// filtering sampler, returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled depth-only view to sample.</param>
	/// <param name="sampler">Filtering sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="CreateBindGroup(GPUBindGroupLayoutHandle, ReadOnlySpan{GPUBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="StdFilteringDepthTexture2DLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"depth"</c> or
	/// to check if the sampler is a filtering sampler is made, so if it isn't, a WebGPU
	/// validation error may happen.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, depth-only view.
	/// </exception>
	public GPUBindGroup CreateStdFilteringDepthTexture2DBindGroup(GPUTextureViewHandle view, GPUSamplerHandle sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if ((view.Usage & TextureUsage.TextureBinding) == 0)
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format is TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32float))
			throw new ArgumentException("view must be a depth-only format", nameof(view));
		return CreateBindGroup(StdFilteringDepthTexture2DLayout, [
			new GPUBindGroupEntry(Binding: 0, new GPUTextureViewBindingResource(view)),
			new GPUBindGroupEntry(Binding: 1, new GPUSamplerBindingResource(sampler))
		]);
	}

	/// <summary>
	/// Creates a texture+sampler bind group for a 2D depth-only texture view and a
	/// comparison sampler, returning an owning object.
	/// </summary>
	/// <param name="view">2D non-multisampled depth-only view to sample.</param>
	/// <param name="sampler">Comparison sampler to pair with the view.</param>
	/// <remarks>
	/// <para>
	/// Convenience wrapper over <see cref="CreateBindGroup(GPUBindGroupLayoutHandle, ReadOnlySpan{GPUBindGroupEntry})"/>.
	/// The returned bind group matches <see cref="StdComparisonDepthTexture2DLayout"/>.
	/// </para>
	/// <para>
	/// No attempt to check if the view's color format's sample type is <c>"depth"</c> or
	/// to check if the sampler is a comparison sampler is made, so if it isn't, a WebGPU
	/// validation error may happen.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown if <paramref name="view"/> or <paramref name="sampler"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="view"/> is not a <see cref="TextureUsage.TextureBinding"/>-enabled,
	/// 2D, non-multisampled, depth-only view.
	/// </exception>
	public GPUBindGroup CreateStdComparisonDepthTexture2DBindGroup(GPUTextureViewHandle view, GPUSamplerHandle sampler) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if ((view.Usage & TextureUsage.TextureBinding) == 0)
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format is TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth32float))
			throw new ArgumentException("view must be a depth-only format", nameof(view));
		return CreateBindGroup(StdComparisonDepthTexture2DLayout, [
			new GPUBindGroupEntry(Binding: 0, new GPUTextureViewBindingResource(view)),
			new GPUBindGroupEntry(Binding: 1, new GPUSamplerBindingResource(sampler))
		]);
	}
}
