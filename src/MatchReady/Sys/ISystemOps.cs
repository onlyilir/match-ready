namespace MatchReady.Sys;

/// <summary>Snapshot data from the process list. Getting it opens no handle to any process.</summary>
public sealed record ProcInfo(string Name, int Pid, int SessionId);

public sealed record PowerPlan(Guid Id, string Name);

public sealed record RegValue(bool Exists, int Value);

/// <summary>
/// Everything the booster does to Windows. The real implementation guards every
/// close/kill/stop/start call with <see cref="Safety.Guard"/>; tests swap in a fake.
/// </summary>
public interface ISystemOps
{
    // Processes
    IReadOnlyList<ProcInfo> ListProcesses();
    string? GetProcessPath(ProcInfo p);
    /// <summary>Asks the app to close its windows (like clicking X). Returns true if a window was asked.</summary>
    bool RequestClose(ProcInfo p);
    bool Kill(ProcInfo p);
    void LaunchUnelevated(string exePath);
    /// <summary>True if the exe's manifest asks for admin, so starting it would show a UAC prompt. Reads the file only.</summary>
    bool ExeNeedsAdmin(string exePath);

    // Services
    /// <summary>Null if the service doesn't exist. Uses the service list only; opens no handle to the service.</summary>
    string? GetServiceStatus(string name);
    void StopService(string name, TimeSpan timeout);
    void StartService(string name, TimeSpan timeout);

    // Power plans
    IReadOnlyList<PowerPlan> ListPowerPlans();
    Guid? GetActivePowerPlan();
    void SetActivePowerPlan(Guid id);
    Guid? DuplicatePowerPlan(Guid source);
    void DeletePowerPlan(Guid id);

    // Per-user registry (HKCU only)
    RegValue ReadUserDword(string key, string name);
    void WriteUserDword(string key, string name, int value);
    void DeleteUserValue(string key, string name);

    // Facts
    /// <summary>Free physical memory right now (0 if unknown).</summary>
    long AvailableMemoryBytes();
    bool? SystemDriveIsSsd();
    bool PrintQueueIsEmpty();
}
