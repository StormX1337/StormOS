using StormOS.Core.Common;
using StormOS.Core.Ipc;

namespace StormOS.Security.Ipc;

/// <summary>Authorization requirements of an IPC operation.</summary>
/// <param name="RequiredTrust">Minimum trust level.</param>
/// <param name="Mutating">Whether the operation changes system state (audit logged).</param>
/// <param name="Cost">Rate limiter tokens consumed per call.</param>
public sealed record OperationPolicy(ClientTrustLevel RequiredTrust, bool Mutating = false, int Cost = 1);

/// <summary>The allow-list of operations and their policies. Unknown operations are always rejected.</summary>
public static class OperationPolicies
{
    private static readonly Dictionary<string, OperationPolicy> Policies = new(StringComparer.Ordinal)
    {
        [IpcOperations.Hello] = new(ClientTrustLevel.Untrusted),
        [IpcOperations.Health] = new(ClientTrustLevel.Untrusted),
        [IpcOperations.TelemetrySubscribe] = new(ClientTrustLevel.LocalUser),
        [IpcOperations.TelemetryUnsubscribe] = new(ClientTrustLevel.LocalUser),
        [IpcOperations.TelemetrySnapshot] = new(ClientTrustLevel.LocalUser),
        [IpcOperations.HardwareInventory] = new(ClientTrustLevel.LocalUser, Cost: 5),
        [IpcOperations.FramesStatus] = new(ClientTrustLevel.LocalUser),
        [IpcOperations.FramesStart] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 5),
        [IpcOperations.FramesStop] = new(ClientTrustLevel.TrustedClient, Mutating: true),
        [IpcOperations.GamesRunning] = new(ClientTrustLevel.LocalUser),
        [IpcOperations.OptimizationRules] = new(ClientTrustLevel.LocalUser, Cost: 5),
        [IpcOperations.OptimizationDetect] = new(ClientTrustLevel.LocalUser, Cost: 2),
        [IpcOperations.OptimizationHistory] = new(ClientTrustLevel.LocalUser, Cost: 2),
        [IpcOperations.OptimizationApply] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 10),
        [IpcOperations.OptimizationRestore] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 10),
        [IpcOperations.OptimizationRestoreAll] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 20),
        [IpcOperations.ProcessSetPriority] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 5),
        [IpcOperations.ProcessTerminate] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 10),
        [IpcOperations.ServicesList] = new(ClientTrustLevel.LocalUser, Cost: 5),
        [IpcOperations.LogsTail] = new(ClientTrustLevel.TrustedClient, Cost: 5),
        [IpcOperations.SessionsList] = new(ClientTrustLevel.LocalUser, Cost: 2),
        [IpcOperations.BenchmarkGaming] = new(ClientTrustLevel.TrustedClient, Mutating: true, Cost: 10),
        [IpcOperations.MetricHistory] = new(ClientTrustLevel.LocalUser, Cost: 5),
    };

    /// <summary>Gets all registered operation names.</summary>
    public static IReadOnlyCollection<string> Operations => Policies.Keys;

    /// <summary>Looks up the policy of an operation.</summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="policy">The policy.</param>
    /// <returns><see langword="true"/> when the operation is allow-listed.</returns>
    public static bool TryGet(string operation, out OperationPolicy policy)
    {
        if (Policies.TryGetValue(operation, out var found))
        {
            policy = found;
            return true;
        }

        policy = new OperationPolicy(ClientTrustLevel.TrustedClient);
        return false;
    }

    /// <summary>Authorizes an operation for a client.</summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="client">The client identity.</param>
    /// <returns>Success, or an error describing why the call is rejected.</returns>
    public static Result Authorize(string operation, ClientIdentity client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!TryGet(operation, out var policy))
        {
            return Result.Failure(StormErrorCodes.UnknownOperation, "The requested operation is not supported by this service version.");
        }

        return client.Trust >= policy.RequiredTrust
            ? Result.Success
            : Result.Failure(StormErrorCodes.Unauthorized, "This action is only available from the installed STORM OS application.");
    }
}
