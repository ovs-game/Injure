// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Injure.ModKit.MonoMod;

internal sealed class GenerationPatchSet : IStrongRefDroppable {
	private List<PatchDeclaration>? patches = new();

	public int Count {
		get {
			chk();
			return patches.Count;
		}
	}

	public void Add(PatchDeclaration patch) {
		ArgumentNullException.ThrowIfNull(patch);
		chk();
		patches.Add(patch);
	}

	public PatchDeclaration[] Snapshot() {
		chk();
		return patches.Count == 0 ? Array.Empty<PatchDeclaration>() : patches.ToArray();
	}

	public void DropStrongReferences() {
		if (patches is null)
			return;
		foreach (PatchDeclaration p in patches)
			p.DropStrongReferences();
		patches.Clear();
		patches = null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[MemberNotNull(nameof(patches))]
	private void chk() {
		if (patches is null)
			throw new InternalStateException("patch set strong ref has already been dropped");
	}
}
