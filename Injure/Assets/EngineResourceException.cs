// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public sealed class EngineResourceException : Exception {
	public EngineResourceID ResourceID { get; }

	public EngineResourceException(EngineResourceID resourceID, string message) : base($"{resourceID}: {message}") {
		ResourceID = resourceID;
	}

	public EngineResourceException(EngineResourceID resourceID, string message, Exception ex) : base($"{resourceID}: {message}", ex) {
		ResourceID = resourceID;
	}
}
