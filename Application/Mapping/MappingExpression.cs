using System.Linq.Expressions;

namespace Application.Mapping
{
    /// <summary>Fluent options of one map, returned by <see cref="MapProfile.CreateMap{TSource, TDestination}"/>.</summary>
    public sealed class MappingExpression<TSource, TDestination>
    {
        private readonly TypeMap _map;
        private readonly MapProfile _profile;

        internal MappingExpression(TypeMap map, MapProfile profile)
        {
            _map = map;
            _profile = profile;
        }

        /// <summary>
        /// Sets how one destination member is filled: <c>o.MapFrom(s => …)</c> (the value is converted to the member's type
        /// like any other value, nested maps included) or <c>o.Ignore()</c> (left as the constructor set it).
        /// </summary>
        public MappingExpression<TSource, TDestination> ForMember<TMember>(
            Expression<Func<TDestination, TMember>> destinationMember, Action<MemberOptions<TSource>> options)
        {
            ArgumentNullException.ThrowIfNull(destinationMember);
            ArgumentNullException.ThrowIfNull(options);
            var name = MemberName(destinationMember);
            var o = new MemberOptions<TSource>();
            options(o);
            if (!o.Ignored && o.Resolver == null)
                throw new ArgumentException($"ForMember({name}) needs o.MapFrom(...) or o.Ignore().", nameof(options));
            _map.Rules[name] = new MemberRule(o.Ignored, o.Resolver, o.ResolverType);
            return this;
        }

        /// <summary>Runs after the members are mapped (for values that need several source fields or a calculation).</summary>
        public MappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _map.AfterMaps.Add((s, d) => action((TSource)s, (TDestination)d));
            return this;
        }

        /// <summary>Replaces the whole member-by-member mapping with <paramref name="converter"/>.</summary>
        public void ConvertUsing(Func<TSource, TDestination> converter)
        {
            ArgumentNullException.ThrowIfNull(converter);
            _map.Converter = s => converter((TSource)s);
        }

        /// <summary>Also declares the map back from <typeparamref name="TDestination"/> to <typeparamref name="TSource"/>
        /// (by name convention; the <c>ForMember</c> rules of this map are not reversed).</summary>
        public MappingExpression<TDestination, TSource> ReverseMap() => _profile.CreateMap<TDestination, TSource>();

        private static string MemberName(LambdaExpression lambda)
        {
            var body = lambda.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u ? u.Operand : lambda.Body;
            if (body is MemberExpression m && m.Expression == lambda.Parameters[0]) return m.Member.Name;
            throw new ArgumentException($"ForMember needs a member of the destination, like d => d.Name (got {lambda}).");
        }
    }

    /// <summary>The options of one destination member (<see cref="MappingExpression{TSource, TDestination}.ForMember"/>).</summary>
    public sealed class MemberOptions<TSource>
    {
        internal Func<object, object?>? Resolver { get; private set; }
        internal Type? ResolverType { get; private set; }
        internal bool Ignored { get; private set; }

        /// <summary>Takes the member's value from <paramref name="resolver"/>.</summary>
        public void MapFrom<TValue>(Func<TSource, TValue> resolver)
        {
            ArgumentNullException.ThrowIfNull(resolver);
            Resolver = s => resolver((TSource)s);
            ResolverType = typeof(TValue);
            Ignored = false;
        }

        /// <summary>Leaves the member alone.</summary>
        public void Ignore()
        {
            Ignored = true;
            Resolver = null;
            ResolverType = null;
        }
    }
}
