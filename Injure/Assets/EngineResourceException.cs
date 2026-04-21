// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Exception thrown when an engine resource load operation fails.
/// </summary>
public sealed class EngineResourceException : Exception {
	/// <summary>
	/// ID of the engine resource involved in the failed operation.
	/// </summary>
	public EngineResourceID ResourceID { get; }

	public EngineResourceException(EngineResourceID resourceID, string message) : base($"{resourceID}: {message}") {
		ResourceID = resourceID;
	}

	public EngineResourceException(EngineResourceID resourceID, string message, Exception ex) : base($"{resourceID}: {message}", ex) {
		ResourceID = resourceID;
	}
}
