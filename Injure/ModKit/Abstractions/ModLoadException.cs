// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Abstractions;

public /* unsealed */ class ModLoadException : Exception {
	public string? ModOwnerID { get; }

	public ModLoadException(string message) : base(message) {
	}

	public ModLoadException(string modOwnerID, string message) : base($"while loading mod '{modOwnerID}': {message}") {
		ModOwnerID = modOwnerID;
	}
}
