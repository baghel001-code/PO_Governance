namespace Application.Mapping
{
    /// <summary>
    /// Object-to-object mapper (our own, replaces the AutoMapper package). Get it from DI
    /// (<c>services.AddMapping()</c>) or from <see cref="MapperConfiguration.CreateMapper"/>.
    /// The maps are declared in <see cref="MapProfile"/> classes. The mapper is thread-safe and meant to be a singleton.
    /// </summary>
    public interface IMapper
    {
        /// <summary>Maps <paramref name="source"/> (its run-time type) to a new <typeparamref name="TDestination"/>.
        /// Collections map element by element: <c>Map&lt;List&lt;UserDto&gt;&gt;(users)</c>.</summary>
        TDestination Map<TDestination>(object? source);

        /// <summary>Maps <paramref name="source"/> to a new <typeparamref name="TDestination"/>.</summary>
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>Maps <paramref name="source"/> onto an existing <paramref name="destination"/> and returns it.
        /// A <c>null</c> source leaves the destination unchanged.</summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);

        /// <summary>Maps <paramref name="source"/> to a new object of <paramref name="destinationType"/> (non-generic callers).</summary>
        object? Map(object? source, Type destinationType);
    }
}
