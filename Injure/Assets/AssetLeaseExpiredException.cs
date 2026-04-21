// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

/// <summary>
/// Exception thrown by an <see cref="IRevokable"/> on usage attempts after its origin
/// lease has been reclaimed.
/// </summary>
public sealed class AssetLeaseExpiredException(string? message = null) : Exception(message) {
}
