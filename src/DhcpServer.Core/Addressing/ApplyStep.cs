namespace DhcpServer.Core.Addressing
{
    /// <summary>The named steps of applying and restoring settings; the name is what the user sees on failure.</summary>
    public enum ApplyStep
    {
        Capture,
        WriteRecord,
        SetAddress,
        AddAddress,
        SetDns,
        ClearDns,
        SetAddressDhcp,
        SetDnsDhcp,
        Verify,
        WaitForAddress,
        DeleteRecord
    }

    public sealed class StepResult
    {
        private StepResult(ApplyStep step, bool succeeded, int exitCode)
        {
            Step = step;
            Succeeded = succeeded;
            ExitCode = exitCode;
        }

        public ApplyStep Step { get; }

        public bool Succeeded { get; }

        public int ExitCode { get; }

        public static StepResult Ok(ApplyStep step)
        {
            return new StepResult(step, true, 0);
        }

        public static StepResult Fail(ApplyStep step, int exitCode = 1)
        {
            return new StepResult(step, false, exitCode);
        }
    }
}
