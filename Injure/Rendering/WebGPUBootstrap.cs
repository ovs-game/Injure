// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;

namespace Injure.Rendering;

public readonly unsafe struct WebGPUBootstrapResult(IRenderSurfaceSource surfaceSource,
		Instance *instance, Surface *surface, Adapter *adapter, Device *device, Queue *queue) {
	public readonly IRenderSurfaceSource SurfaceSource = surfaceSource;
	public readonly Instance *Instance = instance;
	public readonly Surface *Surface = surface;
	public readonly Adapter *Adapter = adapter;
	public readonly Device *Device = device;
	public readonly Queue *Queue = queue;
}

public sealed unsafe class WebGPUBootstrap {
	// ==========================================================================
	// internal types
	private sealed class Request<TStatus, TObject> where TStatus : unmanaged, Enum where TObject : unmanaged {
		public int Done;
		public TStatus Status;
		public TObject *Object;
		public string Message = "<no message available>";
	}

	// ==========================================================================
	// internal objects
	private static readonly WebGPU webgpu = WebGPU.GetApi();
	private int state = (int)State.NotStarted;
	private int pendingCancel = 0;
	private ExceptionDispatchInfo? ex;
	private WebGPUBootstrapResult result;

	// ==========================================================================
	// public api
	public enum State {
		NotStarted = 0,
		Running = 1,
		Completed = 2,
		Cancelled = 3,
		Failed = 4,
	}
	public State CurrentState => (State)Volatile.Read(ref state);

	public void Start(IRenderSurfaceSource surfaceSource) {
		if (Interlocked.CompareExchange(ref state, (int)State.Running, (int)State.NotStarted) != (int)State.NotStarted)
			throw new InvalidOperationException("bootstrap already running/finished");
		Thread t = new Thread(() => {
			Instance *instance = null;
			Surface *surface = null;
			Adapter *adapter = null;
			Device *device = null;
			Queue *queue = null;
			try {
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				InstanceDescriptor instDesc = default;
				instance = WebGPUException.Check(webgpu.CreateInstance(&instDesc));
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				SurfaceDescriptorContainer sdc;
				surfaceSource.CreateSurfaceDesc(&sdc);
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				surface = WebGPUException.Check(webgpu.InstanceCreateSurface(instance, &sdc.Desc));
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				adapter = requestAdapterBlocking(instance, surface);
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				device = requestDeviceBlocking(instance, adapter);
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				queue = WebGPUException.Check(webgpu.DeviceGetQueue(device));
				if (Volatile.Read(ref pendingCancel) == 1) { Volatile.Write(ref state, (int)State.Cancelled); return; }
				result = new WebGPUBootstrapResult(surfaceSource, instance, surface, adapter, device, queue);
				Volatile.Write(ref state, (int)State.Completed);
				queue = null;
				device = null;
				adapter = null;
				surface = null;
				instance = null;
			} catch (Exception caught) {
				ex = ExceptionDispatchInfo.Capture(caught);
				Volatile.Write(ref state, (int)State.Failed);
			} finally {
				if (queue is not null) webgpu.QueueRelease(queue);
				if (device is not null) webgpu.DeviceRelease(device);
				if (adapter is not null) webgpu.AdapterRelease(adapter);
				if (surface is not null) webgpu.SurfaceRelease(surface);
				if (instance is not null) webgpu.InstanceRelease(instance);
			}
		}) { IsBackground = true };
		t.Start();
	}

	public void Cancel() => Volatile.Write(ref pendingCancel, 1);

	public WebGPUBootstrapResult GetResultOrThrow() {
		int s = Volatile.Read(ref state);
		if (s == (int)State.Failed)
			(ex ?? throw new InternalStateException("state is Failed but ExceptionDispatchInfo isn't set")).Throw();
		if (s != (int)State.Completed)
			throw new InvalidOperationException($"bootstrap not completed (state: {(State)s})");
		return result;
	}

	// ==========================================================================
	// resource creation
	private static void waitRequest(Instance *instance, ref int done, string opName, int timeoutMs = 10000) {
		SpinWait sw = new SpinWait();
		long start = Environment.TickCount64;
		while (Volatile.Read(ref done) == 0) {
			webgpu.InstanceProcessEvents(instance);
			if (Environment.TickCount64 - start > timeoutMs)
				throw new WebGPUException(opName, $"waiting for callback timed out (waited {timeoutMs} ms)");
			if (sw.NextSpinWillYield)
				Thread.Sleep(1);
			else
				sw.SpinOnce();
		}
	}

	private static Adapter *requestAdapterBlocking(Instance *instance, Surface *surface) {
		Request<RequestAdapterStatus, Adapter> req = new Request<RequestAdapterStatus, Adapter>();
		GCHandle h = GCHandle.Alloc(req);
		try {
			RequestAdapterOptions opts = default;
			opts.CompatibleSurface = surface;
			opts.PowerPreference = PowerPreference.HighPerformance;

			PfnRequestAdapterCallback cb =
				(delegate *unmanaged[Cdecl] <RequestAdapterStatus, Adapter *, byte *, void *, void>)&adapterRequestedCallback;
			webgpu.InstanceRequestAdapter(instance, &opts, cb, (void *)GCHandle.ToIntPtr(h));
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

	private static Device *requestDeviceBlocking(Instance *instance, Adapter *adapter) {
		Request<RequestDeviceStatus, Device> req = new Request<RequestDeviceStatus, Device>();
		GCHandle h = GCHandle.Alloc(req);
		try {
			DeviceDescriptor desc = default;
			PfnRequestDeviceCallback cb =
				(delegate *unmanaged[Cdecl] <RequestDeviceStatus, Device *, byte *, void *, void>)&deviceRequestedCallback;
			webgpu.AdapterRequestDevice(adapter, &desc, cb, (void *)GCHandle.ToIntPtr(h));
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
}
