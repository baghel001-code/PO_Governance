namespace Application.Mapping
{
    /// <summary>
    /// A group of maps. Derive from it and declare the maps in the constructor:
    /// <code>
    /// public sealed class UserProfile : MapProfile
    /// {
    ///     public UserProfile()
    ///     {
    ///         CreateMap&lt;AppUser, UserDto&gt;()
    ///             .ForMember(d => d.DisplayName, o => o.MapFrom(s => s.Name ?? s.Email))
    ///             .ForMember(d => d.PasswordHash, o => o.Ignore());
    ///     }
    /// }
    /// </code>
    /// <c>services.AddMapping()</c> finds every public, non-abstract profile with a parameterless constructor in the
    /// given assemblies (default: <c>Application</c>).
    /// </summary>
    public abstract class MapProfile
    {
        internal List<TypeMap> TypeMaps { get; } = new();

        /// <summary>Name shown in configuration errors.</summary>
        public virtual string ProfileName => GetType().Name;

        /// <summary>
        /// Declares a map from <typeparamref name="TSource"/> to <typeparamref name="TDestination"/>. By convention each
        /// writable destination property / field takes the source property / field of the same name (case-insensitive);
        /// <see cref="MappingExpression{TSource, TDestination}.ForMember"/> changes that per member.
        /// </summary>
        public MappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
        {
            var map = new TypeMap(typeof(TSource), typeof(TDestination), ProfileName);
            TypeMaps.Add(map);
            return new MappingExpression<TSource, TDestination>(map, this);
        }
    }

    /// <summary>The profile behind <c>new MapperConfiguration(cfg => cfg.CreateMap&lt;A, B&gt;())</c>.</summary>
    internal sealed class InlineProfile : MapProfile
    {
        public override string ProfileName => "inline configuration";
    }
}
