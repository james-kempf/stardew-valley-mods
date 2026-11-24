using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PerformanceBundle.Core;

/// <summary>Chooses which game methods PreJit compiles. Reflection only; safe to call from a background thread.</summary>
internal static class PreJitScope
{
    /// <summary>Location, NPC and rendering namespaces (research.md 3.5: 326 types, about 5,400 methods).</summary>
    public static readonly string[] DefaultNamespaces =
    {
        "StardewValley.Locations", "StardewValley.Characters", "StardewValley.Monsters", "StardewValley.TerrainFeatures",
        "StardewValley.BellsAndWhistles", "StardewValley.Objects", "StardewValley.Pathfinding"
    };

    public static readonly string[] DefaultTypes =
    {
        "StardewValley.GameLocation", "StardewValley.NPC", "StardewValley.Character", "StardewValley.Farmer",
        "StardewValley.Object", "StardewValley.Item", "StardewValley.Event"
    };

    private const BindingFlags AllDeclared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>A type is in scope if its outermost declaring type is listed or sits in a listed namespace (or below it).</summary>
    public static bool IsTypeInScope(Type type, IReadOnlyCollection<string> namespaces, IReadOnlyCollection<string> typeNames)
    {
        Type outer = type;
        while (outer.DeclaringType != null)
            outer = outer.DeclaringType;
        if (outer.FullName != null && typeNames.Contains(outer.FullName))
            return true;
        string? ns = outer.Namespace;
        return ns != null && namespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));
    }

    /// <summary>Whether RuntimeHelpers.PrepareMethod can compile this method without throwing or side effects.</summary>
    public static bool IsCandidate(MethodBase method)
    {
        if (method is System.Reflection.Emit.DynamicMethod)
            return false;
        if (method.IsAbstract || method.ContainsGenericParameters || (method.DeclaringType?.ContainsGenericParameters ?? true))
            return false;
        // Type initializers are skipped so PreJit never risks running one off the main thread (Farm..cctor loads a texture).
        if (method is ConstructorInfo && method.IsStatic)
            return false;
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            return false;
        MethodImplAttributes impl = method.MethodImplementationFlags;
        if ((impl & MethodImplAttributes.InternalCall) != 0 || (impl & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL)
            return false;
        return true;
    }

    public static List<MethodBase> Collect(Assembly assembly, IReadOnlyCollection<string> namespaces, IReadOnlyCollection<string> typeNames)
    {
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        var result = new List<MethodBase>();
        foreach (Type? type in types)
        {
            if (type == null || type.IsInterface || !IsTypeInScope(type, namespaces, typeNames))
                continue;
            try
            {
                foreach (MethodInfo m in type.GetMethods(AllDeclared))
                    if (IsCandidate(m))
                        result.Add(m);
                foreach (ConstructorInfo c in type.GetConstructors(AllDeclared))
                    if (IsCandidate(c))
                        result.Add(c);
            }
            catch (Exception)
            {
                // A type whose members can't be resolved (missing optional dependency) is skipped.
            }
        }
        return result;
    }
}
