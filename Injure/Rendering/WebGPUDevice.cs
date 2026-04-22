// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using WebGPU;
using static WebGPU.WebGPU;

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
		public TStatus Status;
		public TObject Object;
		public string? Message;
		public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
	}

	private sealed class DeviceLostCallbackState {
		public required WebGPUDevice Owner;
	}

	// ==========================================================================
	// internal objects / properties
	internal readonly WGPUInstance Instance;
	internal readonly WGPUAdapter Adapter;
	internal readonly WGPUDevice Device;
	internal readonly WGPUQueue Queue;

	private readonly GCHandle deviceLostCallbackStateHandle;

	private readonly GPUBindGroupLayout globalsUniformBindGroupLayout;
	private readonly GPUBindGroupLayout colorTex2DBindGroupLayout;
	private readonly GPUBindGroupLayout filteringDepthTex2DBindGroupLayout;
	private readonly GPUBindGroupLayout comparisonDepthTex2DBindGroupLayout;

	private int disposed = 0;
	private int lost = 0;
	private DeviceLostInfo? lostInfo = null;

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
	public GPUBindGroupLayoutRef StdGlobalsUniformLayout { get { chk(); return field; } }

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
	public GPUBindGroupLayoutRef StdColorTexture2DLayout { get { chk(); return field; } }

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
	public GPUBindGroupLayoutRef StdFilteringDepthTexture2DLayout { get { chk(); return field; } }

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
	public GPUBindGroupLayoutRef StdComparisonDepthTexture2DLayout { get { chk(); return field; } }

	/// <summary>
	/// Creates a <see cref="WebGPUDevice"/>.
	/// </summary>
	/// <param name="compatibleSurface"><c>compatibleSurface</c> to pass to adapter creation.</param>
	/// <param name="powerPreference"><c>powerPreference</c> to pass to adapter creation.</param>
	/// <param name="backendType"><c>backendType</c> to pass to adapter creation.</param>
	public WebGPUDevice(WGPUSurface compatibleSurface = default,
		WGPUPowerPreference powerPreference = WGPUPowerPreference.HighPerformance,
		WGPUBackendType backendType = WGPUBackendType.Undefined) {
		WGPUInstanceDescriptor instDesc = default;
		Instance = Check(wgpuCreateInstance(&instDesc));
		Adapter = requestAdapterBlocking(Instance, compatibleSurface, powerPreference, backendType);
		Device = requestDeviceBlocking(this, Adapter, out deviceLostCallbackStateHandle);
		Queue = Check(wgpuDeviceGetQueue(Device));

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
	private static WGPUAdapter requestAdapterBlocking(WGPUInstance instance, WGPUSurface compatibleSurface,
		WGPUPowerPreference powerPreference, WGPUBackendType backendType) {
		Request<WGPURequestAdapterStatus, WGPUAdapter> req = new Request<WGPURequestAdapterStatus, WGPUAdapter>();
		GCHandle h = GCHandle.Alloc(req);
		try {
			WGPURequestAdapterOptions opts = new WGPURequestAdapterOptions {
				compatibleSurface = compatibleSurface,
				powerPreference = powerPreference,
				backendType = backendType
			};
			WGPURequestAdapterCallbackInfo cb = new WGPURequestAdapterCallbackInfo {
				mode = WGPUCallbackMode.AllowSpontaneous,
				callback = &adapterRequestCallback,
				userdata1 = (void *)GCHandle.ToIntPtr(h)
			};
			WGPUFuture future = wgpuInstanceRequestAdapter(instance, &opts, cb);
			req.Done.Wait();
			if (req.Status != WGPURequestAdapterStatus.Success || req.Object.IsNull)
				throw new WebGPUException("wgpuInstanceRequestAdapter", req.Message ?? req.Status.ToString());
			return req.Object;
		} finally {
			h.Free();
		}
	}

	[UnmanagedCallersOnly]
	private static void adapterRequestCallback(WGPURequestAdapterStatus status, WGPUAdapter adapter, WGPUStringView message,
		void *userdata1, void *userdata2) {
		GCHandle h = GCHandle.FromIntPtr((IntPtr)userdata1);
		Request<WGPURequestAdapterStatus, WGPUAdapter> req = (Request<WGPURequestAdapterStatus, WGPUAdapter>)h.Target!;
		req.Status = status;
		req.Object = adapter;
		req.Message = message.ToString();
		req.Done.Set();
	}

	private static WGPUDevice requestDeviceBlocking(WebGPUDevice owner, WGPUAdapter adapter, out GCHandle lostCallbackStateHandle) {
		Request<WGPURequestDeviceStatus, WGPUDevice> req = new Request<WGPURequestDeviceStatus, WGPUDevice>();
		DeviceLostCallbackState st = new DeviceLostCallbackState { Owner = owner };
		GCHandle reqHandle = GCHandle.Alloc(req);
		GCHandle stHandle = GCHandle.Alloc(st);
		try {
			WGPUDeviceDescriptor desc = new WGPUDeviceDescriptor {
				deviceLostCallbackInfo = new WGPUDeviceLostCallbackInfo {
					mode = WGPUCallbackMode.AllowSpontaneous,
					callback = &deviceLostCallback,
					userdata1 = (void *)GCHandle.ToIntPtr(stHandle)
				}
			};
			WGPURequestDeviceCallbackInfo cb = new WGPURequestDeviceCallbackInfo {
				mode = WGPUCallbackMode.AllowSpontaneous,
				callback = &deviceRequestCallback,
				userdata1 = (void *)GCHandle.ToIntPtr(reqHandle)
			};
			WGPUFuture future = wgpuAdapterRequestDevice(adapter, &desc, cb);
			req.Done.Wait();
			if (req.Status != WGPURequestDeviceStatus.Success || req.Object.IsNull)
				throw new WebGPUException("wgpuAdapterRequestDevice", req.Message ?? req.Status.ToString());
			lostCallbackStateHandle = stHandle;
			stHandle = default;
			return req.Object;
		} finally {
			if (stHandle.IsAllocated)
				stHandle.Free();
			reqHandle.Free();
		}
	}

	[UnmanagedCallersOnly]
	private static void deviceRequestCallback(WGPURequestDeviceStatus status, WGPUDevice device, WGPUStringView message,
		void *userdata1, void *userdata2) {
		GCHandle h = GCHandle.FromIntPtr((IntPtr)userdata1);
		Request<WGPURequestDeviceStatus, WGPUDevice> req = (Request<WGPURequestDeviceStatus, WGPUDevice>)h.Target!;
		req.Status = status;
		req.Object = device;
		req.Message = message.ToString();
		req.Done.Set();
	}

	[UnmanagedCallersOnly]
	private static void deviceLostCallback(WGPUDevice *device, WGPUDeviceLostReason reason, WGPUStringView message,
		void *userdata1, void *userdata2) {
		GCHandle h = GCHandle.FromIntPtr((nint)userdata1);
		DeviceLostCallbackState st = (DeviceLostCallbackState)h.Target!;
		DeviceLossEventReason r = DeviceLossEventReason.Enum.FromTag((DeviceLossEventReason.Case)((int)reason - 1)); // TODO this fucking sucks
		st.Owner.NotifyLost(new DeviceLostInfo(DeviceLossInfoKind.Final, r, message.ToString()));
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
		chk();
		WGPUBufferDescriptor desc = new WGPUBufferDescriptor {
			size = size,
			usage = usage.ToWebGPUType(),
			mappedAtCreation = mappedAtCreation
		};
		return new GPUBuffer(Check(wgpuDeviceCreateBuffer(Device, &desc)), size, usage);
	}

	/// <summary>
	/// Writes a single unmanaged value into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged value type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="val">Value to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBufferHandle buffer, ulong offset, in T val) where T : unmanaged {
		chk();
		fixed (T *p = &val)
			wgpuQueueWriteBuffer(Queue, buffer.WGPUBuffer, offset, p, (nuint)sizeof(T));
	}

	/// <summary>
	/// Writes data from a span into a GPU buffer.
	/// </summary>
	/// <typeparam name="T">Unmanaged element type to upload.</typeparam>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Data to upload.</param>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write. Empty spans are accepted and
	/// are a no-op.
	/// </remarks>
	public void WriteToBuffer<T>(GPUBufferHandle buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged {
		chk();
		if (data.IsEmpty)
			return;
		fixed (T *p = data)
			wgpuQueueWriteBuffer(Queue, buffer.WGPUBuffer, offset, p, (nuint)(data.Length * sizeof(T)));
	}

	/// <summary>
	/// Writes data from a pointer into a GPU buffer.
	/// </summary>
	/// <param name="buffer">Destination buffer.</param>
	/// <param name="offset">Byte offset into <paramref name="buffer"/>.</param>
	/// <param name="data">Pointer to the data to upload.</param>
	/// <param name="size">Number of bytes to upload from <paramref name="data"/>.</param>
	/// <remarks>
	/// This is a queue write, not a mapped-buffer write. The pointer only needs to remain
	/// valid for the duration of the call.
	/// </remarks>
	public void WriteToBuffer(GPUBufferHandle buffer, ulong offset, void *data, nuint size) {
		chk();
		wgpuQueueWriteBuffer(Queue, buffer.WGPUBuffer, offset, data, size);
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
		chk();

		ReadOnlySpan<TextureFormat> viewFormats = @params.ViewFormats.IsDefault ? ReadOnlySpan<TextureFormat>.Empty : @params.ViewFormats.AsSpan();
		WGPUTextureFormat[] wgpuViewFormats;
		if (viewFormats.Length > 0) {
			wgpuViewFormats = new WGPUTextureFormat[viewFormats.Length];
			HashSet<TextureFormat> tmp = new HashSet<TextureFormat>(viewFormats.Length);
			for (int i = 0; i < viewFormats.Length; i++) {
				if (viewFormats[i] == @params.Format)
					throw new ArgumentException("ViewFormats must not contain the texture's format", nameof(@params));
				if (!tmp.Add(viewFormats[i]))
					throw new ArgumentException("ViewFormats must not contain duplicates", nameof(@params));
				wgpuViewFormats[i] = viewFormats[i].ToWebGPUType();
			}
		}

		fixed (WGPUTextureFormat *p = wgpuViewFormats) {
			WGPUTextureDescriptor desc = new WGPUTextureDescriptor {
				size = new WGPUExtent3D {
					width = @params.Width,
					height = @params.Height,
					depthOrArrayLayers = @params.DepthOrArrayLayers
				},
				mipLevelCount = @params.MipLevelCount,
				sampleCount = @params.SampleCount,
				dimension = @params.Dimension.ToWebGPUType(),
				format = @params.Format.ToWebGPUType(),
				usage = @params.Usage.ToWebGPUType(),
				viewFormatCount = (nuint)viewFormats.Length,
				viewFormats = p
			};
			WGPUTexture tex = Check(wgpuDeviceCreateTexture(Device, &desc));
			try {
				// TODO: think about whether default view creation should be external
				// so that the try-catch isn't necessary
				return new GPUTexture(tex, @params.Width, @params.Height, @params.DepthOrArrayLayers,
					@params.MipLevelCount, @params.SampleCount, @params.Dimension, @params.Format, @params.Usage,
					viewFormats.ToArray());
			} catch (WebGPUException) {
				wgpuTextureRelease(tex);
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
		chk();
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
		chk();
		WGPUTexelCopyTextureInfo copyDst = new WGPUTexelCopyTextureInfo {
			texture = tex.WGPUTexture,
			mipLevel = dst.MipLevel,
			origin = new WGPUOrigin3D {
				x = dst.X,
				y = dst.Y,
				z = dst.Z,
			},
			aspect = dst.Aspect.ToWebGPUType()
		};
		WGPUTexelCopyBufferLayout dataLayout = new WGPUTexelCopyBufferLayout {
			offset = layout.Offset,
			bytesPerRow = layout.BytesPerRow,
			rowsPerImage = layout.RowsPerImage
		};
		WGPUExtent3D texSize = new WGPUExtent3D {
			width = dst.Width,
			height = dst.Height,
			depthOrArrayLayers = dst.DepthOrArrayLayers
		};
		wgpuQueueWriteTexture(Queue, &copyDst, data, size, &dataLayout, &texSize);
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
		chk();
		if (@params.LodMinClamp < 0)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMinClamp cannot be negative");
		if (@params.LodMaxClamp < @params.LodMinClamp)
			throw new ArgumentOutOfRangeException(nameof(@params), "LodMaxClamp cannot be smaller than LodMinClamp");
		if (@params.MaxAnisotropy < 1)
			throw new ArgumentOutOfRangeException(nameof(@params), "MaxAnisotropy must be at least 1");
		if (@params.MaxAnisotropy > 1 &&
			(@params.MinFilter != FilterMode.Linear || @params.MagFilter != FilterMode.Linear || @params.MipmapFilter != MipmapFilterMode.Linear))
			throw new ArgumentException("MinFilter/MagFilter/MipMapFilter must be set to Linear if MaxAnisotropy > 1", nameof(@params));
		WGPUSamplerDescriptor desc = new WGPUSamplerDescriptor {
			addressModeU = @params.AddressModeU.ToWebGPUType(),
			addressModeV = @params.AddressModeV.ToWebGPUType(),
			addressModeW = @params.AddressModeW.ToWebGPUType(),
			magFilter = @params.MagFilter.ToWebGPUType(),
			minFilter = @params.MinFilter.ToWebGPUType(),
			mipmapFilter = @params.MipmapFilter.ToWebGPUType(),
			lodMinClamp = @params.LodMinClamp,
			lodMaxClamp = @params.LodMaxClamp,
			compare = @params.Compare.ToWebGPUType(),
			maxAnisotropy = @params.MaxAnisotropy
		};
		return new GPUSampler(Check(wgpuDeviceCreateSampler(Device, &desc)));
	}

	private static WGPUBindGroupLayoutEntry toRawBindGroupLayoutEntry(in GPUBindGroupLayoutEntry entry) {
		WGPUBindGroupLayoutEntry raw = new WGPUBindGroupLayoutEntry {
			binding = entry.Binding,
			visibility = entry.Visibility.ToWebGPUType()
		};
		switch (entry.Layout) {
		case GPUBufferBindingLayout b:
			raw.buffer = new WGPUBufferBindingLayout {
				type = b.Type.ToWebGPUType(),
				hasDynamicOffset = b.HasDynamicOffset,
				minBindingSize = b.MinBindingSize
			};
			return raw;
		case GPUSamplerBindingLayout s:
			raw.sampler = new WGPUSamplerBindingLayout {
				type = s.Type.ToWebGPUType()
			};
			return raw;
		case GPUStorageTextureBindingLayout st:
			raw.storageTexture = new WGPUStorageTextureBindingLayout {
				access = st.Access.ToWebGPUType(),
				format = st.Format.ToWebGPUType(),
				viewDimension = st.ViewDimension.ToWebGPUType()
			};
			return raw;
		case GPUTextureBindingLayout t:
			raw.texture = new WGPUTextureBindingLayout {
				sampleType = t.SampleType.ToWebGPUType(),
				viewDimension = t.ViewDimension.ToWebGPUType(),
				multisampled = t.Multisampled
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
		chk();
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

		WGPUBindGroupLayoutEntry *rawEntries = stackalloc WGPUBindGroupLayoutEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupLayoutEntry(entries[i]);
		WGPUBindGroupLayoutDescriptor desc = new WGPUBindGroupLayoutDescriptor {
			entryCount = (nuint)entries.Length,
			entries = rawEntries
		};
		return new GPUBindGroupLayout(Check(wgpuDeviceCreateBindGroupLayout(Device, &desc)));
	}

	private static WGPUBindGroupEntry toRawBindGroupEntry(in GPUBindGroupEntry entry) {
		WGPUBindGroupEntry raw = new WGPUBindGroupEntry {
			binding = entry.Binding
		};
		switch (entry.Resource) {
		case GPUBufferBindingResource b:
			raw.buffer = b.Buffer.WGPUBuffer;
			raw.offset = b.Offset;
			raw.size = b.Size ?? (b.Buffer.Size - b.Offset);
			return raw;
		case GPUSamplerBindingResource s:
			raw.sampler = s.Sampler.WGPUSampler;
			return raw;
		case GPUTextureViewBindingResource v:
			raw.textureView = v.View.WGPUTextureView;
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
		chk();
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

		WGPUBindGroupEntry *rawEntries = stackalloc WGPUBindGroupEntry[entries.Length];
		for (int i = 0; i < entries.Length; i++)
			rawEntries[i] = toRawBindGroupEntry(entries[i]);
		WGPUBindGroupDescriptor desc = new WGPUBindGroupDescriptor {
			layout = layout.WGPUBindGroupLayout,
			entryCount = (nuint)entries.Length,
			entries = rawEntries
		};
		return new GPUBindGroup(Check(wgpuDeviceCreateBindGroup(Device, &desc)));
	}

	/// <summary>
	/// Creates a shader module from WGSL source code, returning an owning object.
	/// </summary>
	/// <param name="source">WGSL source code.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="source"/> is empty.</exception>
	public GPUShaderModule CreateShaderModuleWGSL(ReadOnlySpan<char> source) {
		chk();
		if (source.IsEmpty)
			throw new ArgumentException("WGSL source code must not be empty", nameof(source));

		int bytes = Encoding.UTF8.GetByteCount(source);
		Span<byte> utf8 = bytes <= 1024 ? stackalloc byte[bytes] : new byte[bytes];
		int written = Encoding.UTF8.GetBytes(source, utf8);
		fixed (byte *p = utf8) {
			WGPUShaderSourceWGSL src = new WGPUShaderSourceWGSL {
				chain = new WGPUChainedStruct {
					sType = WGPUSType.ShaderSourceWGSL,
					next = null
				},
				code = new WGPUStringView(p, written)
			};
			WGPUShaderModuleDescriptor desc = new WGPUShaderModuleDescriptor {
				nextInChain = &src.chain
			};
			return new GPUShaderModule(Check(wgpuDeviceCreateShaderModule(Device, &desc)));
		}
	}

	/// <summary>
	/// Creates a shader module from SPIR-V code, returning an owning object.
	/// </summary>
	/// <param name="code">SPIR-V code as 32-bit words.</param>
	/// <exception cref="ArgumentException">Thrown if <paramref name="code"/> is empty.</exception>
	public GPUShaderModule CreateShaderModuleSPIRV(ReadOnlySpan<uint> code) {
		chk();
		if (code.IsEmpty)
			throw new ArgumentException("SPIR-V code must not be empty", nameof(code));

		fixed (uint *p = code) {
			WGPUShaderSourceSPIRV src = new WGPUShaderSourceSPIRV {
				chain = new WGPUChainedStruct {
					sType = WGPUSType.ShaderSourceSPIRV,
					next = null
				},
				codeSize = (uint)code.Length,
				code = p
			};
			WGPUShaderModuleDescriptor desc = new WGPUShaderModuleDescriptor {
				nextInChain = &src.chain
			};
			return new GPUShaderModule(Check(wgpuDeviceCreateShaderModule(Device, &desc)));
		}
	}

	/// <summary>
	/// Creates a shader module from SPIR-V code, returning an owning object.
	/// </summary>
	/// <param name="code">
	/// SPIR-V code as 32-bit words encoded in little-endian byte order.
	/// Bytes 0..3 encode word 0, bytes 4..7 encode word 1, and so on.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown if <paramref name="code"/> is empty or if its length is not a
	/// multiple of 4.
	/// </exception>
	public GPUShaderModule CreateShaderModuleSPIRV(ReadOnlySpan<byte> code) {
		chk();
		if (code.IsEmpty)
			throw new ArgumentException("SPIR-V code must not be empty", nameof(code));
		if ((code.Length & 0b11) != 0)
			throw new ArgumentException("byte length of SPIR-V code encoded as bytes must be a multiple of 4", nameof(code));

		Span<uint> words = (code.Length <= 1024) ? stackalloc uint[code.Length >> 2] : new uint[code.Length >> 2];
		for (int i = 0; i < words.Length; i++)
			words[i] = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(i << 2, 4));
		return CreateShaderModuleSPIRV(words);
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
		chk();
		if (layouts.IsEmpty)
			throw new ArgumentException("pipeline layout must contain at least one bind group layout");

		WGPUBindGroupLayout *bgLayouts = stackalloc WGPUBindGroupLayout[layouts.Length];
		for (int i = 0; i < layouts.Length; i++) {
			ArgumentNullException.ThrowIfNull(layouts[i]);
			bgLayouts[i] = layouts[i].WGPUBindGroupLayout;
		}

		WGPUPipelineLayoutDescriptor desc = new WGPUPipelineLayoutDescriptor {
			bindGroupLayoutCount = (nuint)layouts.Length,
			bindGroupLayouts = bgLayouts
		};
		return new GPUPipelineLayout(Check(wgpuDeviceCreatePipelineLayout(Device, &desc)));
	}

	/// <summary>
	/// Creates a render pipeline, returning an owning object.
	/// </summary>
	/// <param name="params">Render pipeline creation parameters.</param>
	public GPURenderPipeline CreateRenderPipeline(in GPURenderPipelineCreateParams @params) {
		// -----------------------------------------------------------------
		// validation
		chk();

		ArgumentNullException.ThrowIfNull(@params.Layout);
		ArgumentNullException.ThrowIfNull(@params.Vertex.ShaderModule);
		ArgumentException.ThrowIfNullOrWhiteSpace(@params.Vertex.EntryPoint);

		bool haveFrag = false;
		VertexState vert = @params.Vertex;
		FragmentState frag = @params.Fragment ?? default;
		PrimitiveState prim = @params.Primitive ?? new PrimitiveState();
		MultisampleState ms = @params.Multisample ?? new MultisampleState();

		if (@params.Fragment is FragmentState f) {
			haveFrag = true;
			frag = f;
			ArgumentNullException.ThrowIfNull(f.ShaderModule);
			ArgumentException.ThrowIfNullOrWhiteSpace(f.EntryPoint);
			if (f.Targets.IsEmpty)
				throw new ArgumentException("fragment state must have at least one color target", nameof(@params));
		}

		bool stripTopo = prim.Topology.Tag is PrimitiveTopology.Case.LineStrip or PrimitiveTopology.Case.TriangleStrip;
		if (stripTopo && prim.StripIndexFormat == IndexFormat.Undefined)
			throw new ArgumentException("strip topologies must specify a strip index format", nameof(@params));
		if (!stripTopo && prim.StripIndexFormat != IndexFormat.Undefined)
			throw new ArgumentException("strip index format is only valid for strip topologies", nameof(@params));

		// -----------------------------------------------------------------
		// upfront allocations, as a workaround for CS8346
		ReadOnlySpan<VertexBufferLayout> vertBuffers = vert.Buffers.IsDefault ? ReadOnlySpan<VertexBufferLayout>.Empty : vert.Buffers.AsSpan();
		int totalVertAttrCount = 0;
		for (int i = 0; i < vertBuffers.Length; i++)
			totalVertAttrCount += vertBuffers[i].Attributes.Length;

		ReadOnlySpan<ColorTargetState> colorTargets = (haveFrag && !frag.Targets.IsDefault) ? frag.Targets.AsSpan() : ReadOnlySpan<ColorTargetState>.Empty;

		WGPUVertexBufferLayout *wgpuVertBuffers = stackalloc WGPUVertexBufferLayout[vertBuffers.Length];
		WGPUVertexAttribute *wgpuVertAttrs = stackalloc WGPUVertexAttribute[totalVertAttrCount];
		WGPUColorTargetState *wgpuColorTargets = stackalloc WGPUColorTargetState[colorTargets.Length];
		WGPUBlendState *wgpuBlendStates = stackalloc WGPUBlendState[colorTargets.Length];

		int vsEntrySize = Encoding.UTF8.GetByteCount(vert.EntryPoint);
		byte *vsEntry = stackalloc byte[vsEntrySize];
		int vsEntryLen = Encoding.UTF8.GetBytes(vert.EntryPoint, new Span<byte>(vsEntry, vsEntrySize));

		int fsEntrySize = haveFrag ? Encoding.UTF8.GetByteCount(frag.EntryPoint) : 0;
		byte *fsEntry = stackalloc byte[fsEntrySize];
		int fsEntryLen = haveFrag ? Encoding.UTF8.GetBytes(frag.EntryPoint, new Span<byte>(fsEntry, fsEntrySize)) : 0;

		// -----------------------------------------------------------------
		// vertex
		int attrBase = 0;
		for (int i = 0; i < vertBuffers.Length; i++) {
			VertexBufferLayout vb = vertBuffers[i];
			ReadOnlySpan<VertexAttribute> vbAttributes = vb.Attributes.IsDefault ? ReadOnlySpan<VertexAttribute>.Empty : vb.Attributes.AsSpan();
			for (int j = 0; j < vbAttributes.Length; j++)
				wgpuVertAttrs[attrBase + j] = vbAttributes[j].ToWebGPUType();
			wgpuVertBuffers[i] = new WGPUVertexBufferLayout {
				arrayStride = vb.ArrayStride,
				stepMode = vb.StepMode.ToWebGPUType(),
				attributeCount = (nuint)vbAttributes.Length,
				attributes = vbAttributes.IsEmpty ? null : (wgpuVertAttrs + attrBase)
			};
			attrBase += vbAttributes.Length;
		}

		WGPUVertexState wgpuVert = new WGPUVertexState {
			module = vert.ShaderModule.WGPUShaderModule,
			entryPoint = new WGPUStringView(vsEntry, vsEntryLen),
			constantCount = 0, // TODO
			constants = null,  // TODO
			bufferCount = (nuint)vertBuffers.Length,
			buffers = vertBuffers.IsEmpty ? null : wgpuVertBuffers
		};

		// -----------------------------------------------------------------
		// fragment
		WGPUFragmentState wgpuFrag = default;
		WGPUFragmentState *pWgpuFrag = null;
		if (haveFrag) {
			for (int i = 0; i < colorTargets.Length; i++)
				wgpuColorTargets[i] = colorTargets[i].ToWebGPUType(&wgpuBlendStates[i]);
			wgpuFrag = new WGPUFragmentState {
				module = frag.ShaderModule.WGPUShaderModule,
				entryPoint = new WGPUStringView(fsEntry, fsEntryLen),
				constantCount = 0, // TODO
				constants = null,  // TODO
				targetCount = (nuint)colorTargets.Length,
				targets = colorTargets.IsEmpty ? null : wgpuColorTargets
			};
			pWgpuFrag = &wgpuFrag;
		}

		// -----------------------------------------------------------------
		// primitive, depth/stencil, multisample
		WGPUPrimitiveState wgpuPrim = prim.ToWebGPUType();

		WGPUDepthStencilState wgpuDepthStencil = default;
		WGPUDepthStencilState *pWgpuDepthStencil = null;
		if (@params.DepthStencil is DepthStencilState ds) {
			wgpuDepthStencil = ds.ToWebGPUType();
			pWgpuDepthStencil = &wgpuDepthStencil;
		}

		WGPUMultisampleState wgpuMs = ms.ToWebGPUType();

		// -----------------------------------------------------------------
		// full pipeline descriptor
		WGPURenderPipelineDescriptor desc = new WGPURenderPipelineDescriptor {
			layout = @params.Layout.WGPUPipelineLayout,
			vertex = wgpuVert,
			primitive = wgpuPrim,
			depthStencil = pWgpuDepthStencil,
			multisample = wgpuMs,
			fragment = pWgpuFrag
		};
		return new GPURenderPipeline(Check(wgpuDeviceCreateRenderPipeline(Device, &desc)));
	}

	/// <summary>
	/// Releases all owned GPU state and invalidates all objects created from this <see cref="WebGPUDevice"/>.
	/// </summary>
	public void Dispose() {
		if (Interlocked.Exchange(ref disposed, 1) != 0)
			return;

		filteringDepthTex2DBindGroupLayout.Dispose();
		comparisonDepthTex2DBindGroupLayout.Dispose();
		colorTex2DBindGroupLayout.Dispose();
		globalsUniformBindGroupLayout.Dispose();
		deviceLostCallbackStateHandle.Free();
		wgpuQueueRelease(Queue);
		wgpuDeviceRelease(Device);
		wgpuAdapterRelease(Adapter);
		wgpuInstanceRelease(Instance);
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
		chk();
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float
			or TextureFormat.Case.Depth24PlusStencil8 or TextureFormat.Case.Depth32FloatStencil8 or TextureFormat.Case.Stencil8)
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
		chk();
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
		chk();
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float))
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
		chk();
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(sampler);
		if (view.Usage.HasNone(TextureUsage.TextureBinding))
			throw new ArgumentException("view must have TextureBinding set in its usages", nameof(view));
		if (view.Dimension != TextureViewDimension.Dimension2D)
			throw new ArgumentException("view must be 2D", nameof(view));
		if (view.SampleCount != 1)
			throw new ArgumentException("view must not be multisampled", nameof(view));
		if (!(view.Format.Tag is TextureFormat.Case.Depth16Unorm or TextureFormat.Case.Depth24Plus or TextureFormat.Case.Depth32Float))
			throw new ArgumentException("view must be a depth-only format", nameof(view));
		return CreateBindGroup(StdComparisonDepthTexture2DLayout, [
			new GPUBindGroupEntry(Binding: 0, new GPUTextureViewBindingResource(view)),
			new GPUBindGroupEntry(Binding: 1, new GPUSamplerBindingResource(sampler))
		]);
	}

	// ==========================================================================
	// internal api surface / hooks

	/// <summary>
	/// Submits a finished command buffer to the queue.
	/// </summary>
	/// <remarks>
	/// Renderer-internal submission interface used by <see cref="RenderFrame"/>.
	/// Callers are expected to have finished all encoding / passes before this point.
	/// </remarks>
	internal void Submit(WGPUCommandBuffer commands) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("attempted to Submit() after dispose");
		wgpuQueueSubmit(Queue, 1, &commands);
	}

	/// <summary>
	/// Notifies this <see cref="WebGPUDevice"/> that its underlying <see cref="WGPUDevice"/>
	/// has been lost.
	/// </summary>
	internal void NotifyLost(DeviceLostInfo info) {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("attempted to NotifyLost() after dispose");
		lostInfo = info;
		Volatile.Write(ref lost, 1); // memory fence for the above one
	}

	/// <summary>
	/// Throws a <see cref="DeviceLostException"/> with the currently stored
	/// device loss information. If the device has not been lost, throws
	/// <see cref="InternalStateException"/>. Never returns.
	/// </summary>
	[DoesNotReturn]
	internal void TripLostException() {
		if (Volatile.Read(ref disposed) != 0)
			throw new InternalStateException("TripLostException() called post-dispose");
		if (Volatile.Read(ref lost) == 0)
			throw new InternalStateException("TripLostException() called while the device isn't lost");
		DeviceLostInfo info = Volatile.Read(ref lostInfo) ?? throw new InternalStateException("device lost but no DeviceLostInfo present");
		throw new DeviceLostException(info);
	}

	// ==========================================================================
	// lifetime/dispose
	private void chk() {
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		if (Volatile.Read(ref lost) != 0) {
			DeviceLostInfo info = Volatile.Read(ref lostInfo) ?? throw new InternalStateException("device lost but no DeviceLostInfo present");
			throw new DeviceLostException(info);
		}
	}
}
