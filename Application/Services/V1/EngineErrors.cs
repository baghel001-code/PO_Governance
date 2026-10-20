using DecisionEngine.Common;
using Domain.Exceptions;

namespace Application.Services.V1
{
    /// <summary>
    /// Bridges DecisionEngine's rule errors to the platform's exceptions: the engine layer knows nothing about
    /// HTTP or <see cref="AppException"/>, so the use-case services translate <see cref="EngineValidationException"/>
    /// into <see cref="AppValidationException"/> (400) and <see cref="EngineNotFoundException"/> into
    /// <see cref="NotFoundException"/> (404), keeping the message (the v1.1.4 wording).
    /// </summary>
    public static class EngineErrors
    {
        /// <summary>The platform exception for an engine error.</summary>
        public static AppException Translate(DecisionEngineException ex) => ex switch
        {
            EngineNotFoundException => new NotFoundException(ex.Message),
            _ => new AppValidationException(ex.Message)
        };

        /// <summary>Runs an engine call, translating its rule errors.</summary>
        public static T Run<T>(Func<T> call)
        {
            try { return call(); }
            catch (DecisionEngineException ex) { throw Translate(ex); }
        }

        /// <summary>Runs an engine call, translating its rule errors.</summary>
        public static void Run(Action call)
        {
            try { call(); }
            catch (DecisionEngineException ex) { throw Translate(ex); }
        }
    }
}
