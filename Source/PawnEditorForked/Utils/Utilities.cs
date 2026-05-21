using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace PawnEditor;

/// <summary>
/// General-purpose extension methods and helpers used throughout Pawn Editor.
/// Includes list manipulation, delegate creation, and string formatting utilities.
/// </summary>
public static class Utilities
{
    /// <summary>Safely sets a value at the given index, extending the list with defaults if needed.</summary>
    public static void Set<T>(this List<T> list, int index, T item)
    {
        while (list.Count <= index) list.Add(default);
        list[index] = item;
    }

    /// <summary>Gets a value at the given index with wraparound (negative indices count from the end).</summary>
    public static T Get<T>(this List<T> list, int index)
    {
        while (index >= list.Count) index -= list.Count;
        while (index < 0) index += list.Count;
        return list[index];
    }

    /// <summary>Deconstructs an array into 1 element (for pattern matching).</summary>
    public static void Deconstruct<T>(this T[] items, out T t0)
    {
        t0 = items.Length > 0 ? items[0] : default;
    }

    /// <summary>Deconstructs an array into 2 elements (for pattern matching).</summary>
    public static void Deconstruct<T>(this T[] items, out T t0, out T t1)
    {
        t0 = items.Length > 0 ? items[0] : default;
        t1 = items.Length > 1 ? items[1] : default;
    }

    /// <summary>Deconstructs an array into 3 elements (for pattern matching).</summary>
    public static void Deconstruct<T>(this T[] items, out T t0, out T t1, out T t2)
    {
        t0 = items.Length > 0 ? items[0] : default;
        t1 = items.Length > 1 ? items[1] : default;
        t2 = items.Length > 2 ? items[2] : default;
    }

    /// <summary>Filters out elements present in the given HashSet. More efficient than LINQ Except for sets.</summary>
    public static IEnumerable<T> Except<T>(this IEnumerable<T> source, HashSet<T> without) =>
        source.Where(v => !without.Contains(v));

    /// <summary>Projects dictionary values while keeping the same keys.</summary>
    public static Dictionary<TKey, TResult> SelectValues<TKey, TSource, TResult>(
        this Dictionary<TKey, TSource> source,
        Func<TKey, TSource, TResult> selector) =>
        source.Select(kv => new KeyValuePair<TKey, TResult>(kv.Key, selector(kv.Key, kv.Value)))
              .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>Returns true if the source is not null and contains at least one match.</summary>
    public static bool NotNullAndAny<T>(this IEnumerable<T> source, Func<T, bool> predicate) =>
        source != null && source.Any(predicate);

    /// <summary>Increments a float value by a step, clamped to [min, max] and rounded to 2 decimals.</summary>
    public static float StepValue(float oldValuePct, float stepPct, float min = 0, float max = 1) =>
        Mathf.Clamp((float)Math.Round(oldValuePct + stepPct, 2), min, max);

    /// <summary>Creates a typed delegate from a MethodInfo (static methods).</summary>
    public static T CreateDelegate<T>(this MethodInfo info) where T : Delegate =>
        (T)info.CreateDelegate(typeof(T));

    /// <summary>Creates a typed delegate from a MethodInfo bound to a target instance.</summary>
    public static T CreateDelegate<T>(this MethodInfo info, object target) where T : Delegate =>
        (T)info.CreateDelegate(typeof(T), target);

    /// <summary>
    /// Converts CamelCase or PascalCase to sentence case.
    /// Example: "CamelCase" → "Camel case", "SkinColorOverride" → "Skin color override"
    /// </summary>
    public static string ConvertCamelCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new StringBuilder(input.Length * 2);
        result.Append(char.ToUpper(input[0]));

        for (var i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]))
                result.Append(' ');
            result.Append(char.ToLower(input[i]));
        }

        return result.ToString();
    }

    /// <summary>
    /// Creates a delegate that casts the first argument from object to the method's declaring type.
    /// Used for reflection calls where the instance type isn't known at compile time (mod compat).
    /// </summary>
    public static T CreateDelegateCasting<T>(this MethodInfo info) where T : Delegate
    {
        var parms = info.GetParameters();
        var parmTypes = new Type[parms.Length + 1];
        parmTypes[0] = typeof(object);
        for (var i = 0; i < parms.Length; i++) parmTypes[i + 1] = parms[i].ParameterType;

        var dm = new DynamicMethod("<DelegateFor>__" + info.Name, info.ReturnType, parmTypes);
        var gen = dm.GetILGenerator();
        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Castclass, info.ReflectedType);
        for (var i = 1; i < parmTypes.Length; i++) gen.Emit(OpCodes.Ldarg, i);
        gen.Emit(OpCodes.Callvirt, info);
        gen.Emit(OpCodes.Ret);
        return dm.CreateDelegate<T>();
    }
}
