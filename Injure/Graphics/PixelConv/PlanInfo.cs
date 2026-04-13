// SPDX-License-Identifier: MIT

namespace Injure.Graphics.PixelConv;

public enum PlanExecutionPath : byte {
	Memcpy,
	DedicatedKernel,
	GenericKernel
}

public enum PlanBackend : byte {
	None,
	AVX2,
	SSSE3,
	SSE2,
	AdvSIMD,
	Scalar
}

public readonly struct PlanInfo {
	public PlanExecutionPath ExecutionPath { get; }
	public PlanBackend Backend { get; }
	public bool UsesVectorizedBackend { get; }

	internal PlanInfo(PlanExecutionPath executionPath, PlanBackend backend) {
		ExecutionPath = executionPath;
		Backend = backend;
		UsesVectorizedBackend = !(backend is PlanBackend.None or PlanBackend.Scalar);
	}
}
