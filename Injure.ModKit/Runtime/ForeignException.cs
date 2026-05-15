// SPDX-License-Identifier: MIT

using System;

namespace Injure.ModKit.Runtime;

public sealed class ForeignException(string originalTypeName, string originalMessage) : Exception($"exception of type '{originalTypeName}': {originalMessage}") {
	public string OriginalTypeName { get; } = originalTypeName;
	public string OriginalMessage { get; } = originalMessage;
}
