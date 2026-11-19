namespace Application.Mapping
{
    /// <summary>A missing map, an invalid configuration or a value that cannot be converted.</summary>
    public sealed class MappingException : Exception
    {
        public MappingException(string message) : base(message) { }
        public MappingException(string message, Exception inner) : base(message, inner) { }
    }
}
