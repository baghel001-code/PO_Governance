namespace DecisionEngine.Common
{
    /// <summary>
    /// Base of the rule errors the engines raise (bad / missing input data). Messages are the exact
    /// user-facing v1.1.4 texts. The engine layer has no notion of HTTP: the Application services translate
    /// these into their own exceptions (400 / 404).
    /// </summary>
    public abstract class DecisionEngineException : Exception
    {
        protected DecisionEngineException(string message) : base(message) { }
    }

    /// <summary>The input cannot be evaluated (v1.1.4 showed the message as a toast / validation error).</summary>
    public sealed class EngineValidationException : DecisionEngineException
    {
        public EngineValidationException(string message) : base(message) { }
    }

    /// <summary>The requested item is not in the data snapshot.</summary>
    public sealed class EngineNotFoundException : DecisionEngineException
    {
        public EngineNotFoundException(string message) : base(message) { }
    }
}
