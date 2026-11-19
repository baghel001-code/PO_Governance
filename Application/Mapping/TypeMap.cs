using System.Collections;
using System.Reflection;

namespace Application.Mapping
{
    /// <summary>A ForMember rule: ignore the member, or take its value from a resolver.</summary>
    internal sealed record MemberRule(bool Ignore, Func<object, object?>? Resolver, Type? ResolverType);

    /// <summary>A readable source member (property with a public getter, or public field).</summary>
    internal sealed record SourceMember(string Name, Type Type, Func<object, object?> Get);

    /// <summary>A writable destination member (property with a public setter or init, or public non-readonly field).</summary>
    internal sealed record DestinationMember(string Name, Type Type, Action<object, object?> Set);

    /// <summary>Where a destination member or constructor parameter gets its value from.</summary>
    internal sealed record ValueSource(Func<object, object?> Get, Type Type);

    /// <summary>One planned member assignment.</summary>
    internal sealed record MemberPlan(DestinationMember Destination, ValueSource Source);

    /// <summary>One planned constructor parameter (Source null = the parameter's default value).</summary>
    internal sealed record ParameterPlan(ParameterInfo Parameter, ValueSource? Source);

    /// <summary>
    /// One declared map (source type → destination type): its rules from the profile, and the plan built once by
    /// <see cref="Freeze"/> (constructor, members, what is left unmapped). Read-only after <see cref="Freeze"/>.
    /// </summary>
    internal sealed class TypeMap
    {
        public TypeMap(Type sourceType, Type destinationType, string profileName)
        {
            SourceType = sourceType;
            DestinationType = destinationType;
            ProfileName = profileName;
        }

        public Type SourceType { get; }
        public Type DestinationType { get; }
        public string ProfileName { get; }

        // declared in the profile
        public Dictionary<string, MemberRule> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Action<object, object>> AfterMaps { get; } = new();
        public Func<object, object?>? Converter { get; set; }

        // the plan
        public bool UsesDefaultConstructor { get; private set; }
        public ConstructorInfo? Constructor { get; private set; }
        public ParameterPlan[] ConstructorParameters { get; private set; } = Array.Empty<ParameterPlan>();
        public MemberPlan[] Members { get; private set; } = Array.Empty<MemberPlan>();
        public string[] UnmappedMembers { get; private set; } = Array.Empty<string>();
        public string[] UnknownRules { get; private set; } = Array.Empty<string>();

        public string Name => $"{TypeNames.Of(SourceType)} -> {TypeNames.Of(DestinationType)}";

        public void Freeze()
        {
            if (Converter != null) return;
            var source = MemberAccess.Readable(SourceType);
            var destination = MemberAccess.Writable(DestinationType);

            ValueSource? From(string name)
            {
                if (Rules.TryGetValue(name, out var rule))
                    return rule.Ignore ? null : new ValueSource(rule.Resolver!, rule.ResolverType!);
                return source.TryGetValue(name, out var s) ? new ValueSource(s.Get, s.Type) : null;
            }

            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UsesDefaultConstructor = DestinationType.IsValueType || DestinationType.GetConstructor(Type.EmptyTypes) != null;
            if (!UsesDefaultConstructor && !DestinationType.IsAbstract && !DestinationType.IsInterface)
            {
                // the public constructor with the most parameters that can all be filled (records, immutable classes)
                foreach (var ctor in DestinationType.GetConstructors().OrderByDescending(c => c.GetParameters().Length))
                {
                    var plan = ctor.GetParameters()
                        .Select(p => new ParameterPlan(p, p.Name == null ? null : From(p.Name)))
                        .ToArray();
                    if (plan.All(p => p.Source != null || p.Parameter.HasDefaultValue))
                    {
                        Constructor = ctor;
                        ConstructorParameters = plan;
                        // parameters left at their default value count as mapped too (set by the constructor)
                        foreach (var p in plan) bound.Add(p.Parameter.Name!);
                        break;
                    }
                }
            }

            var members = new List<MemberPlan>();
            var unmapped = new List<string>();
            foreach (var d in destination)
            {
                if (bound.Contains(d.Name)) continue;
                if (Rules.TryGetValue(d.Name, out var rule) && rule.Ignore) continue;
                if (From(d.Name) is { } s) members.Add(new MemberPlan(d, s));
                else unmapped.Add(d.Name);
            }
            Members = members.ToArray();
            UnmappedMembers = unmapped.ToArray();

            var known = new HashSet<string>(destination.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
            known.UnionWith(DestinationType.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name ?? ""));
            UnknownRules = Rules.Keys.Where(k => !known.Contains(k)).ToArray();
        }
    }

    /// <summary>Reflection over the public members of a type (cached per type).</summary>
    internal static class MemberAccess
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyDictionary<string, SourceMember>> ReadableCache = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<DestinationMember>> WritableCache = new();

        public static IReadOnlyDictionary<string, SourceMember> Readable(Type type) => ReadableCache.GetOrAdd(type, t =>
        {
            var map = new Dictionary<string, SourceMember>(StringComparer.OrdinalIgnoreCase);
            // most derived first, so a property hidden with "new" resolves to the derived one
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                               .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                               .OrderByDescending(p => Depth(p.DeclaringType)))
                map.TryAdd(p.Name, new SourceMember(p.Name, p.PropertyType, p.GetValue));
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                map.TryAdd(f.Name, new SourceMember(f.Name, f.FieldType, f.GetValue));
            return map;
        });

        public static IReadOnlyList<DestinationMember> Writable(Type type) => WritableCache.GetOrAdd(type, t =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<DestinationMember>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                               .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                               .OrderByDescending(p => Depth(p.DeclaringType)))
                if (seen.Add(p.Name)) list.Add(new DestinationMember(p.Name, p.PropertyType, p.SetValue));
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly && !f.IsLiteral))
                if (seen.Add(f.Name)) list.Add(new DestinationMember(f.Name, f.FieldType, f.SetValue));
            return list;
        });

        private static int Depth(Type? t)
        {
            var n = 0;
            for (; t != null; t = t.BaseType) n++;
            return n;
        }
    }

    /// <summary>The collection shapes the mapper builds: arrays, List / IList / ICollection / IEnumerable /
    /// IReadOnlyList / IReadOnlyCollection (→ List), HashSet / ISet (→ HashSet). Dictionaries are not collections here.</summary>
    internal static class Collections
    {
        private static readonly Type[] ListLike =
        {
            typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(IEnumerable<>),
            typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>)
        };

        public static bool TryElementType(Type type, out Type element)
        {
            element = typeof(object);
            if (type == typeof(string)) return false;
            if (type.IsArray)
            {
                element = type.GetElementType()!;
                return true;
            }
            if (!type.IsGenericType) return false;
            var def = type.GetGenericTypeDefinition();
            if (!ListLike.Contains(def) && def != typeof(HashSet<>) && def != typeof(ISet<>)) return false;
            element = type.GetGenericArguments()[0];
            return true;
        }

        /// <summary>The element type of any source sequence (null = not known, e.g. a non-generic IEnumerable).</summary>
        public static Type? SourceElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            var e = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? type
                : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return e?.GetGenericArguments()[0];
        }

        public static bool IsSequence(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

        public static object Build(Type target, Type element, IEnumerable<object?> items)
        {
            if (target.IsArray)
            {
                var all = items.ToList();
                var array = Array.CreateInstance(element, all.Count);
                for (var i = 0; i < all.Count; i++) array.SetValue(all[i], i);
                return array;
            }
            var def = target.GetGenericTypeDefinition();
            if (def == typeof(HashSet<>) || def == typeof(ISet<>))
            {
                var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(element))!;
                var add = set.GetType().GetMethod(nameof(HashSet<object>.Add))!;
                foreach (var item in items) add.Invoke(set, new[] { item });
                return set;
            }
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            foreach (var item in items) list.Add(item);
            return list;
        }

        public static object Empty(Type target)
        {
            TryElementType(target, out var element);
            return Build(target, element, Array.Empty<object?>());
        }
    }

    /// <summary>Readable type names for error messages (List&lt;UserDto&gt;, int?).</summary>
    internal static class TypeNames
    {
        public static string Of(Type t)
        {
            if (Nullable.GetUnderlyingType(t) is { } u) return Of(u) + "?";
            if (t.IsArray) return Of(t.GetElementType()!) + "[]";
            if (!t.IsGenericType) return t.Name;
            var name = t.Name[..t.Name.IndexOf('`')];
            return $"{name}<{string.Join(", ", t.GetGenericArguments().Select(Of))}>";
        }
    }
}
