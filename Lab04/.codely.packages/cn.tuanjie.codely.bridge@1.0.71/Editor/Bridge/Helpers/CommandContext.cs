namespace UnityTcp.Editor.Helpers
{
    /// <summary>
    /// Ambient context for the command currently being dispatched. The command loop sets it right
    /// before a handler runs and clears it afterwards, so a <c>HandleCommand(JObject)</c> can read
    /// the native request id (to start a runner job) without changing its prototype.
    ///
    /// Main-thread only — commands are dispatched serially on the editor loop, so a plain static
    /// is sufficient (no thread-local needed).
    /// </summary>
    public static class CommandContext
    {
        /// <summary>Native request id of the command being handled (for EnqueueResponse).</summary>
        public static ulong RequestId { get; private set; }

        /// <summary>Command type of the command being handled (usable as a job name).</summary>
        public static string CommandType { get; private set; }

        public static void Set(ulong requestId, string commandType)
        {
            RequestId = requestId;
            CommandType = commandType;
        }

        public static void Clear()
        {
            RequestId = 0;
            CommandType = null;
        }
    }
}
