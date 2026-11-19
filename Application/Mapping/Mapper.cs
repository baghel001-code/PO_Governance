using System.Globalization;

namespace Application.Mapping
{
    /// <summary>
    /// The <see cref="IMapper"/> over a <see cref="MapperConfiguration"/>.
    /// <para>How a value is converted to a destination member / parameter type, in this order:</para>
    /// <list type="number">
    /// <item><c>null</c> → an empty collection for a collection type, else <c>default</c> (null, 0, false…).</item>
    /// <item>A declared map for the value's run-time type (or a base type / interface) → a new mapped object.</item>
    /// <item><c>object</c> → the value itself; <c>string</c> → the value as text (numbers and dates in the invariant culture).</item>
    /// <item>A sequence into an array / List / IList / ICollection / IEnumerable / IReadOnlyList / IReadOnlyCollection /
    /// HashSet / ISet → a new collection, each element converted (never the source collection itself).</item>
    /// <item>Assignable (same type, base type, interface) → the value itself (a reference, not a copy).</item>
    /// <item><c>T?</c> → as <c>T</c> (a blank string gives null); enums from a name (case-insensitive) or a number,
    /// enum → enum by name, then by number; enum → number; string → Guid; DateTime → DateTimeOffset;
    /// other IConvertible pairs with <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/> (invariant culture).</item>
    /// <item>Anything else → <see cref="MappingException"/> "No map from A to B".</item>
    /// </list>
    /// </summary>
    public sealed class Mapper : IMapper
    {
        /// <summary>Deepest nesting followed before giving up (a reference cycle in the source).</summary>
        internal const int MaxDepth = 64;

        private readonly MapperConfiguration _config;

        public Mapper(MapperConfiguration configuration)
        {
            _config = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>The configuration this mapper uses.</summary>
        public MapperConfiguration Configuration => _config;

        public TDestination Map<TDestination>(object? source) => (TDestination)ConvertValue(source, typeof(TDestination), 0)!;

        public TDestination Map<TSource, TDestination>(TSource source) => (TDestination)ConvertValue(source, typeof(TDestination), 0)!;

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (destination is null) return Map<TSource, TDestination>(source);
            if (source is null) return destination;
            var map = _config.Find(source.GetType(), typeof(TDestination)) ?? throw NoMap(source.GetType(), typeof(TDestination));
            return (TDestination)MapObject(map, source, destination, 0)!;
        }

        public object? Map(object? source, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            return ConvertValue(source, destinationType, 0);
        }

        private object? MapObject(TypeMap map, object source, object? destination, int depth)
        {
            if (map.Converter != null) return map.Converter(source);
            var target = destination ?? Create(map, source, depth);
            foreach (var m in map.Members)
            {
                object? value;
                try
                {
                    value = ConvertValue(m.Source.Get(source), m.Destination.Type, depth + 1);
                    m.Destination.Set(target, value);
                }
                catch (Exception ex) when (ex is not MappingException)
                {
                    throw new MappingException($"{map.Name}, member '{m.Destination.Name}': {ex.Message}", ex);
                }
            }
            foreach (var after in map.AfterMaps) after(source, target);
            return target;
        }

        private object Create(TypeMap map, object source, int depth)
        {
            if (map.UsesDefaultConstructor) return Activator.CreateInstance(map.DestinationType)!;
            if (map.Constructor == null)
                throw new MappingException($"{map.Name}: cannot create {TypeNames.Of(map.DestinationType)} - no usable public constructor.");
            var args = new object?[map.ConstructorParameters.Length];
            for (var i = 0; i < args.Length; i++)
            {
                var p = map.ConstructorParameters[i];
                try
                {
                    args[i] = p.Source == null ? p.Parameter.DefaultValue : ConvertValue(p.Source.Get(source), p.Parameter.ParameterType, depth + 1);
                }
                catch (Exception ex) when (ex is not MappingException)
                {
                    throw new MappingException($"{map.Name}, constructor parameter '{p.Parameter.Name}': {ex.Message}", ex);
                }
            }
            return map.Constructor.Invoke(args);
        }

        private object? ConvertValue(object? value, Type target, int depth)
        {
            if (depth > MaxDepth)
                throw new MappingException($"Mapping went deeper than {MaxDepth} levels - is there a reference cycle in the source?");
            if (value == null)
                return Collections.TryElementType(target, out _) ? Collections.Empty(target) : Default(target);

            var type = value.GetType();
            if (_config.Find(type, target) is { } map) return MapObject(map, value, null, depth);
            if (target == typeof(object)) return value;
            if (target == typeof(string))
                return value as string ?? (value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString());
            if (Collections.IsSequence(type) && Collections.TryElementType(target, out var element))
                return Collections.Build(target, element,
                    ((System.Collections.IEnumerable)value).Cast<object?>().Select(x => ConvertValue(x, element, depth + 1)).ToList());
            if (target.IsAssignableFrom(type)) return value;

            if (Nullable.GetUnderlyingType(target) is { } underlying)
                return value is string s && string.IsNullOrWhiteSpace(s) ? null : ConvertValue(value, underlying, depth);

            try
            {
                if (target.IsEnum)
                {
                    if (value is string name) return Enum.Parse(target, name.Trim(), ignoreCase: true);
                    if (value is Enum && Enum.TryParse(target, value.ToString(), ignoreCase: true, out var byName) && Enum.IsDefined(target, byName!))
                        return byName;
                    return Enum.ToObject(target, Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
                }
                if (target == typeof(Guid) && value is string g) return Guid.Parse(g);
                if (target == typeof(DateTimeOffset) && value is DateTime dt) return new DateTimeOffset(dt);
                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
                    return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                throw new MappingException($"Cannot convert '{value}' ({TypeNames.Of(type)}) to {TypeNames.Of(target)}: {ex.Message}", ex);
            }
            throw NoMap(type, target);
        }

        private static object? Default(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        private static MappingException NoMap(Type source, Type destination) => new(
            $"No map from {TypeNames.Of(source)} to {TypeNames.Of(destination)} - add CreateMap<{TypeNames.Of(source)}, {TypeNames.Of(destination)}>() to a MapProfile.");
    }
}
