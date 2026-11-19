using System.Collections.Concurrent;

namespace Application.Mapping
{
    /// <summary>
    /// The maps of one or more <see cref="MapProfile"/>s, checked and planned once. Build it at start-up
    /// (<c>services.AddMapping()</c> does), then <see cref="CreateMapper"/>. Read-only and thread-safe after construction.
    /// </summary>
    public sealed class MapperConfiguration
    {
        private readonly Dictionary<(Type, Type), TypeMap> _maps = new();
        private readonly ConcurrentDictionary<(Type, Type), TypeMap?> _lookup = new();

        /// <summary>Configuration from profile instances.</summary>
        public MapperConfiguration(IEnumerable<MapProfile> profiles)
        {
            ArgumentNullException.ThrowIfNull(profiles);
            foreach (var profile in profiles)
            foreach (var map in profile.TypeMaps)
            {
                var key = (map.SourceType, map.DestinationType);
                if (_maps.TryGetValue(key, out var existing))
                    throw new MappingException(
                        $"The map {map.Name} is declared twice ({existing.ProfileName} and {map.ProfileName}).");
                _maps[key] = map;
            }
            foreach (var map in _maps.Values) map.Freeze();
        }

        /// <summary>Configuration from profile instances.</summary>
        public MapperConfiguration(params MapProfile[] profiles) : this((IEnumerable<MapProfile>)profiles) { }

        /// <summary>Inline configuration: <c>new MapperConfiguration(cfg => cfg.CreateMap&lt;A, B&gt;())</c>.</summary>
        public MapperConfiguration(Action<MapProfile> configure) : this(Inline(configure)) { }

        /// <summary>The declared maps (source type, destination type).</summary>
        public IReadOnlyCollection<(Type Source, Type Destination)> Maps => _maps.Keys;

        /// <summary>A mapper over this configuration.</summary>
        public IMapper CreateMapper() => new Mapper(this);

        /// <summary>
        /// Throws a <see cref="MappingException"/> listing every problem: a destination member with no source member of the
        /// same name and no ForMember rule, a value that cannot be converted to the member's type (e.g. a nested class without
        /// a map), a ForMember rule for a member that does not exist, a destination that cannot be constructed.
        /// <c>services.AddMapping()</c> runs it at start-up.
        /// </summary>
        public void AssertConfigurationIsValid()
        {
            var errors = new List<string>();
            foreach (var map in _maps.Values.OrderBy(m => m.ProfileName).ThenBy(m => m.Name))
            {
                if (map.Converter != null) continue;
                var where = $"{map.Name} ({map.ProfileName})";
                foreach (var rule in map.UnknownRules)
                    errors.Add($"{where}: ForMember({rule}) - the destination has no member or constructor parameter '{rule}'.");
                if (!map.UsesDefaultConstructor && map.Constructor == null)
                    errors.Add($"{where}: cannot create {TypeNames.Of(map.DestinationType)} - it needs a public parameterless constructor, " +
                               "or a public constructor whose parameters all match source members, or ConvertUsing(...).");
                foreach (var p in map.ConstructorParameters.Where(p => p.Source != null && !CanConvert(p.Source.Type, p.Parameter.ParameterType)))
                    errors.Add($"{where}: constructor parameter '{p.Parameter.Name}' - cannot convert {TypeNames.Of(p.Source!.Type)} to " +
                               $"{TypeNames.Of(p.Parameter.ParameterType)}; add CreateMap<{TypeNames.Of(p.Source.Type)}, {TypeNames.Of(p.Parameter.ParameterType)}>().");
                foreach (var name in map.UnmappedMembers)
                    errors.Add($"{where}: '{name}' has no source member with that name - add " +
                               $"ForMember(d => d.{name}, o => o.MapFrom(s => ...)) or o.Ignore().");
                foreach (var m in map.Members.Where(m => !CanConvert(m.Source.Type, m.Destination.Type)))
                    errors.Add($"{where}: '{m.Destination.Name}' - cannot convert {TypeNames.Of(m.Source.Type)} to {TypeNames.Of(m.Destination.Type)}; " +
                               $"add CreateMap<{TypeNames.Of(m.Source.Type)}, {TypeNames.Of(m.Destination.Type)}>().");
            }
            if (errors.Count > 0)
                throw new MappingException("The mapping configuration is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        /// <summary>The map for a source run-time type (or one of its base types / interfaces) and a destination type.</summary>
        internal TypeMap? Find(Type source, Type destination) => _lookup.GetOrAdd((source, destination), key =>
        {
            var (s, d) = key;
            for (var t = s; t != null; t = t.BaseType)
                if (_maps.TryGetValue((t, d), out var m)) return m;
            foreach (var i in s.GetInterfaces())
                if (_maps.TryGetValue((i, d), out var m)) return m;
            return null;
        });

        /// <summary>Whether a value of <paramref name="from"/> can be converted to <paramref name="to"/> (static mirror of
        /// <see cref="Mapper"/>'s value conversion, used by the validation).</summary>
        internal bool CanConvert(Type from, Type to) => CanConvert(from, to, 0);

        private bool CanConvert(Type from, Type to, int depth)
        {
            if (depth > Mapper.MaxDepth) return false;
            if (from == typeof(object) || to == typeof(object) || to == typeof(string)) return true; // object: known at run time only
            if (Find(from, to) != null) return true;
            if (Collections.IsSequence(from) && Collections.TryElementType(to, out var toElement))
            {
                var fromElement = Collections.SourceElementType(from);
                return fromElement == null || CanConvert(fromElement, toElement, depth + 1);
            }
            if (to.IsAssignableFrom(from)) return true;
            var f = Nullable.GetUnderlyingType(from) ?? from;
            var t = Nullable.GetUnderlyingType(to) ?? to;
            if (f != from || t != to) return CanConvert(f, t, depth + 1);
            if (t.IsEnum) return f == typeof(string) || f.IsEnum || IsIntegral(f);
            if (f.IsEnum) return IsIntegral(t);
            if (t == typeof(Guid)) return f == typeof(string);
            if (t == typeof(DateTimeOffset)) return f == typeof(DateTime);
            return typeof(IConvertible).IsAssignableFrom(f) && typeof(IConvertible).IsAssignableFrom(t);
        }

        private static bool IsIntegral(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte);

        private static MapProfile[] Inline(Action<MapProfile> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            var profile = new InlineProfile();
            configure(profile);
            return new MapProfile[] { profile };
        }
    }
}
