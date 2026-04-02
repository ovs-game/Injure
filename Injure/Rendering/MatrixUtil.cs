// SPDX-License-Identifier: MIT

using System.Numerics;

namespace Injure.Rendering;

public static class MatrixUtil {
	public static Matrix4x4 To4x4(Matrix3x2 m) {
		return new Matrix4x4(
			m.M11, m.M12, 0f, 0f,
			m.M21, m.M22, 0f, 0f,
			0f,    0f,    1f, 0f,
			m.M31, m.M32, 0f, 1f
		);
	}

	public static Matrix4x4 OrthoTopLeft(float w, float h) {
		return new Matrix4x4(
			2f / w, 0f,     0f, 0f,
			0f,    -2f / h, 0f, 0f,
			0f,     0f,     1f, 0f,
			-1f,    1f,     0f, 1f
		);
	}
}
