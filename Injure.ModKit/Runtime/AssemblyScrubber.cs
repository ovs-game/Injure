// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Injure.ModKit.Runtime;

internal static class AssemblyScrubber {
	public static void ScrubInstanceReferenceFields(object obj) {
		ArgumentNullException.ThrowIfNull(obj);
		FieldInfo[] fields;
		try {
			fields = obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
		} catch {
			return; // just swallow, this is best-effort
		}
		foreach (FieldInfo field in fields) {
			if (field.IsLiteral || field.FieldType.IsValueType || field.DeclaringType is null)
				continue;
			try {
				if (!field.IsInitOnly)
					field.SetValue(obj, null);
				else
					ScrubInitonlyInstanceReferenceField(obj, field);
			} catch {
				// just swallow, this is best-effort
			}
		}
	}

	public static void ScrubStaticReferenceFields(Assembly assembly) {
		Type[] types;

		try {
			types = assembly.GetTypes();
		} catch (ReflectionTypeLoadException ex) {
			types = ex.Types.Where(static t => t is not null).ToArray()!;
		}

		foreach (Type type in types) {
			FieldInfo[] fields;
			try {
				fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			} catch {
				continue;
			}
			foreach (FieldInfo field in fields) {
				if (field.IsLiteral || field.FieldType.IsValueType || field.DeclaringType is null)
					continue;
				try {
					if (!field.IsInitOnly)
						field.SetValue(null, null);
					else
						ScrubInitonlyStaticReferenceField(field);
				} catch {
					// just swallow, this is best-effort
				}
			}
		}
	}

	public static void ScrubInitonlyInstanceReferenceField(object obj, FieldInfo field) {
		if (field.IsStatic)
			throw new ArgumentException("not an instance field", nameof(field));
		if (field.FieldType.IsValueType)
			throw new ArgumentException("not a reference field", nameof(field));
		Type declaringType = field.DeclaringType ?? throw new ArgumentException("no declaring type", nameof(field));
		if (!declaringType.IsInstanceOfType(obj))
			throw new ArgumentException("object is not an instance of the declaring type", nameof(obj));

		DynamicMethod dm = new(
			$"{declaringType.FullName}_{field.Name}_scrub_instance",
			typeof(void),
			new[] { typeof(object) },
			declaringType.Module,
			skipVisibility: true
		);
		ILGenerator il = dm.GetILGenerator();

		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Castclass, declaringType);
		il.Emit(OpCodes.Ldflda, field);
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Stind_Ref);
		il.Emit(OpCodes.Ret);

		((Action<object>)dm.CreateDelegate(typeof(Action<object>)))(obj);
	}

	public static void ScrubInitonlyStaticReferenceField(FieldInfo field) {
		if (!field.IsStatic)
			throw new ArgumentException("not a static field", nameof(field));
		if (field.FieldType.IsValueType)
			throw new ArgumentException("not a reference field", nameof(field));
		if (field.DeclaringType is null)
			throw new ArgumentException("no declaring type", nameof(field));

		// avoid clearing before cctor runs in case it assigns it again
		RuntimeHelpers.RunClassConstructor(field.DeclaringType.TypeHandle);

		DynamicMethod dm = new(
			$"{field.DeclaringType.FullName}_{field.Name}_scrub_static",
			typeof(void),
			Type.EmptyTypes,
			field.DeclaringType.Module,
			skipVisibility: true
		);
		ILGenerator il = dm.GetILGenerator();

		il.Emit(OpCodes.Ldsflda, field);
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Stind_Ref);
		il.Emit(OpCodes.Ret);

		((Action)dm.CreateDelegate(typeof(Action)))();
	}
}
