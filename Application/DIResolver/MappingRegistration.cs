using System.Reflection;
using Application.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace Application.DIResolver
{
    /// <summary>DI registration of the object mapper (<see cref="IMapper"/>, our replacement for the AutoMapper package).</summary>
    public static class MappingRegistration
    {
        /// <summary>
        /// Finds every public, non-abstract <see cref="MapProfile"/> with a parameterless constructor in
        /// <paramref name="assemblies"/> (default: the <c>Application</c> assembly), builds one
        /// <see cref="MapperConfiguration"/>, validates it (<see cref="MapperConfiguration.AssertConfigurationIsValid"/>:
        /// a broken map stops the start-up with the list of problems) and registers it and <see cref="IMapper"/> as singletons.
        /// Call it once, with every assembly that holds profiles.
        /// </summary>
        public static IServiceCollection AddMapping(this IServiceCollection services, params Assembly[] assemblies)
        {
            if (assemblies.Length == 0) assemblies = new[] { typeof(MappingRegistration).Assembly };
            var profiles = assemblies.Distinct()
                .SelectMany(a => a.GetExportedTypes())
                .Where(t => typeof(MapProfile).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (MapProfile)Activator.CreateInstance(t)!)
                .ToList();

            var configuration = new MapperConfiguration(profiles);
            configuration.AssertConfigurationIsValid();
            services.AddSingleton(configuration);
            services.AddSingleton<IMapper>(sp => sp.GetRequiredService<MapperConfiguration>().CreateMapper());
            return services;
        }
    }
}
