// SPDX-License-Identifier: MIT

using System;

namespace Injure.Assets;

public sealed class AssetLeaseExpiredException(string? message) : Exception(message) {
}
