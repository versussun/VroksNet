namespace VroksNet.Application.TestRuns.CancelTestRun;

public enum CancelTestRunResult
{
    /// <summary>A queued run is now Cancelled; a running one has been told to stop and ends as Cancelled shortly.</summary>
    Cancelled,
    NotFound,

    /// <summary>The run had already finished.</summary>
    NotActive
}
