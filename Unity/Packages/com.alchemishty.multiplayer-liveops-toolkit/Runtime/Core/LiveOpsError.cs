using System;

namespace MultiplayerLiveOpsToolkit.Core
{
    public enum LiveOpsErrorCode
    {
        ModuleInitializationFailed,
        InitializationCancelled
    }

    public sealed class LiveOpsError
    {
        public LiveOpsErrorCode Code { get; }
        public string ModuleId { get; }
        public string Message { get; }
        public Exception Cause { get; }

        internal LiveOpsError(LiveOpsErrorCode code, string moduleId, string message, Exception cause)
        {
            Code = code;
            ModuleId = moduleId;
            Message = message;
            Cause = cause;
        }
    }
}
