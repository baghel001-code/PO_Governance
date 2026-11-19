using Application.DIResolver;
using Application.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace POGovernance.Tests.Mapping
{
    /// <summary>Our own object mapper (Application/Mapping), which replaced the AutoMapper package.</summary>
    public class MapperTests
    {
        // ── test models ─────────────────────────────────────────────────────
        public enum Role { Buyer, CatHead, Planner, Admin }
        public enum RoleCode { Admin = 10, Planner = 20, Buyer = 30 }

        public class Address { public string? City { get; set; } public string? Pin { get; set; } }
        public class AddressDto { public string? City { get; set; } public string? Pin { get; set; } }

        public class User
        {
            public int Id { get; set; }
            public string? Email { get; set; }
            public string? Name { get; set; }
            public string? Role { get; set; }
            public decimal Limit { get; set; }
            public int? Mcs { get; set; }
            public DateTime Created { get; set; }
            public Address? Home { get; set; }
            public List<Address> Offices { get; set; } = new();
            public List<string>? Tags { get; set; }
            public string PasswordHash { get; set; } = "";
        }

        public class UserDto
        {
            public long Id { get; set; }
            public string? EMAIL { get; set; }               // name match is case-insensitive
            public string? DisplayName { get; set; }
            public Role Role { get; set; }                   // string → enum
            public string? Limit { get; set; }               // decimal → string (invariant)
            public long? Mcs { get; set; }                   // int? → long?
            public DateTimeOffset Created { get; set; }      // DateTime → DateTimeOffset
            public AddressDto? Home { get; set; }            // nested map
            public AddressDto[] Offices { get; set; } = Array.Empty<AddressDto>(); // List → array, element maps
            public IReadOnlyList<string> Tags { get; set; } = new List<string> { "default" }; // null → empty
            public string? PasswordHash { get; set; } = "keep";
            public string? Summary { get; set; }
        }

        public sealed record OrderLine(string Article, int Qty, decimal Price);
        public sealed record OrderLineDto(string Article, long Qty, string Price, string? Note = null);

        public sealed class UserProfile : MapProfile
        {
            public UserProfile()
            {
                CreateMap<Address, AddressDto>().ReverseMap();
                CreateMap<User, UserDto>()
                    .ForMember(d => d.DisplayName, o => o.MapFrom(s => s.Name ?? s.Email))
                    .ForMember(d => d.PasswordHash, o => o.Ignore())
                    .ForMember(d => d.Summary, o => o.Ignore())
                    .AfterMap((s, d) => d.Summary = $"{d.DisplayName} ({d.Role})");
                CreateMap<OrderLine, OrderLineDto>();
            }
        }

        private static IMapper NewMapper() => new MapperConfiguration(new UserProfile()).CreateMapper();

        private static User SampleUser() => new()
        {
            Id = 7, Email = "a.b@vmm.in", Name = null, Role = "planner", Limit = 1234.5m, Mcs = 42,
            Created = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc),
            Home = new Address { City = "Gurugram", Pin = "122001" },
            Offices = new() { new Address { City = "Delhi" }, new Address { City = "Pune" } },
            Tags = null, PasswordHash = "secret"
        };

        // ── conventions and conversions ─────────────────────────────────────
        [Fact]
        public void Maps_by_name_with_conversions_nested_maps_collections_rules_and_after_map()
        {
            var dto = NewMapper().Map<UserDto>(SampleUser());

            Assert.Equal(7L, dto.Id);
            Assert.Equal("a.b@vmm.in", dto.EMAIL);
            Assert.Equal("a.b@vmm.in", dto.DisplayName);              // MapFrom fallback to Email
            Assert.Equal(Role.Planner, dto.Role);                      // case-insensitive enum name
            Assert.Equal("1234.5", dto.Limit);                         // invariant culture
            Assert.Equal(42L, dto.Mcs);
            Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), dto.Created);
            Assert.Equal("Gurugram", dto.Home!.City);
            Assert.Equal(new[] { "Delhi", "Pune" }, dto.Offices.Select(o => o.City));
            Assert.Empty(dto.Tags);                                    // null collection → empty
            Assert.Equal("keep", dto.PasswordHash);                    // ignored: constructor value stays
            Assert.Equal("a.b@vmm.in (Planner)", dto.Summary);         // AfterMap
        }

        [Fact]
        public void Collections_are_new_instances_and_nested_objects_are_new_objects()
        {
            var user = SampleUser();
            user.Tags = new List<string> { "x" };
            var dto = NewMapper().Map<User, UserDto>(user);
            Assert.NotSame(user.Tags, dto.Tags);
            Assert.Equal(new[] { "x" }, dto.Tags);
            Assert.IsType<AddressDto>(dto.Home);
        }

        [Fact]
        public void Maps_a_list_at_the_top_level_and_null_gives_null_or_empty()
        {
            var mapper = NewMapper();
            var list = mapper.Map<List<UserDto>>(new[] { SampleUser(), SampleUser() });
            Assert.Equal(2, list.Count);
            Assert.Null(mapper.Map<UserDto>(null));
            Assert.Empty(mapper.Map<List<UserDto>>(null));
        }

        [Fact]
        public void Maps_onto_an_existing_object()
        {
            var existing = new UserDto { PasswordHash = "old", Summary = "old" };
            var result = NewMapper().Map(SampleUser(), existing);
            Assert.Same(existing, result);
            Assert.Equal(7L, existing.Id);
            Assert.Equal("old", existing.PasswordHash);
            Assert.Same(existing, NewMapper().Map<User?, UserDto>(null, existing)); // null source: unchanged
        }

        [Fact]
        public void Reverse_map_works_by_convention()
        {
            var back = NewMapper().Map<Address>(new AddressDto { City = "Noida", Pin = "201301" });
            Assert.Equal("Noida", back.City);
            Assert.Equal("201301", back.Pin);
        }

        [Fact]
        public void Records_are_built_through_their_constructor_with_converted_values_and_defaults()
        {
            var dto = NewMapper().Map<OrderLineDto>(new OrderLine("1100001234", 12, 499.99m));
            Assert.Equal(new OrderLineDto("1100001234", 12, "499.99"), dto);
        }

        [Fact]
        public void Enum_conversions_by_name_then_number()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            Assert.Equal(RoleCode.Planner, mapper.Map<RoleCode>(Role.Planner));   // enum → enum by name
            Assert.Equal(RoleCode.Admin, mapper.Map<RoleCode>(10));               // number → enum
            Assert.Equal(20, mapper.Map<int>(RoleCode.Planner));                  // enum → number
            Assert.Equal(Role.Admin, mapper.Map<Role>(" admin "));                // string → enum
            Assert.Null(mapper.Map<int?>("  "));                                  // blank → null
            Assert.Equal(15, mapper.Map<int?>("15"));
            Assert.Equal(Guid.Parse("6f1f2a1e-0000-4000-8000-000000000001"), mapper.Map<Guid>("6f1f2a1e-0000-4000-8000-000000000001"));
        }

        [Fact]
        public void Convert_using_replaces_the_member_mapping()
        {
            var mapper = new MapperConfiguration(cfg =>
                cfg.CreateMap<Address, string>().ConvertUsing(a => $"{a.City} {a.Pin}")).CreateMapper();
            Assert.Equal("Agra 282001", mapper.Map<string>(new Address { City = "Agra", Pin = "282001" }));
        }

        // ── errors ──────────────────────────────────────────────────────────
        [Fact]
        public void Missing_map_and_bad_value_throw_MappingException_with_a_clear_message()
        {
            var mapper = new MapperConfiguration(_ => { }).CreateMapper();
            var noMap = Assert.Throws<MappingException>(() => mapper.Map<UserDto>(SampleUser()));
            Assert.Contains("No map from User to UserDto", noMap.Message);
            Assert.Contains("CreateMap<User, UserDto>()", noMap.Message);

            var bad = Assert.Throws<MappingException>(() => mapper.Map<int>("twelve"));
            Assert.Contains("Cannot convert 'twelve' (String) to Int32", bad.Message);
        }

        [Fact]
        public void A_failing_resolver_names_the_map_and_member()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Address, AddressDto>()
                .ForMember(d => d.City, o => o.MapFrom<string>(_ => throw new InvalidOperationException("boom")))).CreateMapper();
            var ex = Assert.Throws<MappingException>(() => mapper.Map<AddressDto>(new Address()));
            Assert.Contains("Address -> AddressDto, member 'City': boom", ex.Message);
        }

        [Fact]
        public void A_reference_cycle_stops_with_an_error_instead_of_a_stack_overflow()
        {
            var mapper = new MapperConfiguration(cfg => cfg.CreateMap<Node, Node2>()).CreateMapper();
            var a = new Node();
            a.Next = a;
            var ex = Assert.Throws<MappingException>(() => mapper.Map<Node2>(a));
            Assert.Contains("reference cycle", ex.Message);
        }

        public class Node { public Node? Next { get; set; } }
        public class Node2 { public Node2? Next { get; set; } }

        // ── validation ──────────────────────────────────────────────────────
        [Fact]
        public void The_profile_used_here_is_valid()
        {
            new MapperConfiguration(new UserProfile()).AssertConfigurationIsValid();
        }

        public class Target
        {
            public string? Name { get; set; }
            public string? Missing { get; set; }
            public AddressDto? Home { get; set; }
        }

        public class Immutable
        {
            public Immutable(string unknownParameter) { Value = unknownParameter; }
            public string Value { get; }
        }

        [Fact]
        public void Validation_lists_every_problem()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<User, Target>().ForMember(d => d.Name, o => o.MapFrom(s => s.Email)); // Missing: no source; Home: no Address map
                cfg.CreateMap<User, Immutable>();                                                    // no usable constructor
            });
            var ex = Assert.Throws<MappingException>(config.AssertConfigurationIsValid);
            Assert.Contains("User -> Target (inline configuration): 'Missing' has no source member", ex.Message);
            Assert.Contains("'Home' - cannot convert Address to AddressDto; add CreateMap<Address, AddressDto>()", ex.Message);
            Assert.Contains("User -> Immutable (inline configuration): cannot create Immutable", ex.Message);
        }

        [Fact]
        public void Declaring_the_same_map_twice_is_an_error()
        {
            var ex = Assert.Throws<MappingException>(() => new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Address, AddressDto>();
                cfg.CreateMap<Address, AddressDto>();
            }));
            Assert.Contains("declared twice", ex.Message);
        }

        [Fact]
        public void ForMember_needs_a_direct_destination_member_and_an_option()
        {
            var cfg = new InlineProbe();
            Assert.Throws<ArgumentException>(() => cfg.CreateMap<User, UserDto>().ForMember(d => d.Home!.City, o => o.Ignore()));
            Assert.Throws<ArgumentException>(() => cfg.CreateMap<User, AddressDto>().ForMember(d => d.City, _ => { }));
        }

        private sealed class InlineProbe : MapProfile { }

        // ── DI ──────────────────────────────────────────────────────────────
        [Fact]
        public void AddMapping_finds_the_profiles_validates_and_registers_a_singleton_mapper()
        {
            using var sp = new ServiceCollection().AddMapping(typeof(MapperTests).Assembly).BuildServiceProvider();
            var mapper = sp.GetRequiredService<IMapper>();
            Assert.Same(mapper, sp.GetRequiredService<IMapper>());
            Assert.Contains((typeof(User), typeof(UserDto)), sp.GetRequiredService<MapperConfiguration>().Maps);
            Assert.Equal(7L, mapper.Map<UserDto>(SampleUser()).Id);
        }

        [Fact]
        public void AddMapping_without_arguments_uses_the_Application_assembly()
        {
            using var sp = new ServiceCollection().AddMapping().BuildServiceProvider();
            Assert.Equal(5, sp.GetRequiredService<IMapper>().Map<int>("5"));
        }
    }
}
