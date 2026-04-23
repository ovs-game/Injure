// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Shared;

internal static class Diagnostics {
	// IJ0001-IJ0099 infrastructure/shared
	// IJ0100-IJ0124 ClosedEnum
	// IJ0125-IJ0149 ClosedFlags
	// IJ0200-IJ0249 StronglyTypedInt
	// TODO: also figure out subranges which also probably means renumbering them Again

#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ClosedEnumInvalidTarget = new(
		id: "IJ0101",
		title: "invalid target for attribute ClosedEnum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMustBeReadonly = new(
		id: "IJ0102",
		title: "ClosedEnum target must be readonly",
		messageFormat: "ClosedEnum target struct '{0}' must be a readonly struct",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidSourceShape = new(
		id: "IJ0103",
		title: "invalid ClosedEnum source shape",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidCaseEnum = new(
		id: "IJ0104",
		title: "invalid ClosedEnum Case enum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumAliasNotSupported = new(
		id: "IJ0105",
		title: "ClosedEnum aliases are not supported",
		messageFormat: "ClosedEnum Case member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumDefaultRule = new(
		id: "IJ0106",
		title: "ClosedEnum default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumSuspiciousZeroName = new(
		id: "IJ0107",
		title: "ClosedEnum member with zero value does not look neutral",
		messageFormat: "ClosedEnum zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorInvalid = new(
		id: "IJ0108",
		title: "invalid ClosedEnum mirror declaration",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorMismatch = new(
		id: "IJ0109",
		title: "ClosedEnum mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidTarget = new(
		id: "IJ0125",
		title: "invalid target for attribute ClosedFlags",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMustBeReadonly = new(
		id: "IJ0126",
		title: "ClosedFlags target must be readonly",
		messageFormat: "ClosedFlags target struct '{0}' must be a readonly struct",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidSourceShape = new(
		id: "IJ0127",
		title: "invalid ClosedFlags source shape",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsInvalidBitsEnum = new(
		id: "IJ0128",
		title: "invalid ClosedFlags Bits enum",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsAliasNotSupported = new(
		id: "IJ0129",
		title: "ClosedFlags aliases are not supported",
		messageFormat: "ClosedFlags Bits member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsBadMemberValue = new(
		id: "IJ0130",
		title: "ClosedFlags members must all be atomic powers of two or ORs of previously declared members",
		messageFormat: "ClosedFlags Bits member '{0}' is not a power of two and does not consist of purely already known power-of-two members",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsDefaultRule = new(
		id: "IJ0131",
		title: "ClosedFlags default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsSuspiciousZeroName = new(
		id: "IJ0132",
		title: "ClosedFlags member with zero value does not look neutral",
		messageFormat: "ClosedFlags zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorInvalid = new(
		id: "IJ0133",
		title: "invalid ClosedFlags mirror declaration",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedFlagsMirrorMismatch = new(
		id: "IJ0134",
		title: "ClosedFlags mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedFlags",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntInvalidTarget = new(
		id: "IJ0201",
		title: "invalid target for attribute StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMustBeReadonly = new(
		id: "IJ0202",
		title: "StronglyTypedInt target must be readonly",
		messageFormat: "StronglyTypedInt target struct '{0}' must be a readonly struct",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntUnsupportedBacking = new(
		id: "IJ0203",
		title: "unsupported backing type for StronglyTypedInt",
		messageFormat: "backing type '{0}' is not supported (supported: int, uint, long, ulong, Int128, UInt128)",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMemberCollision = new(
		id: "IJ0204",
		title: "existing member collides with reserved member for StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
