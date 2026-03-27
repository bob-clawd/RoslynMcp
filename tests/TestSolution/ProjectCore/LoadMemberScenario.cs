namespace ProjectCore;

public interface ILoadMemberScenarioOperation
{
    Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default);
}

public interface ILoadMemberScenarioWithBodyOperation
{
    async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(input);
        await Task.Yield();
        return Complete(normalized);
    }

    private static WorkItem Normalize(WorkItem input)
    {
        return input with { Priority = input.Priority + 100 };
    }

    private static OperationResult Complete(WorkItem input)
    {
        return new OperationResult(true, $"Interface:{input.Name}:{input.Priority}");
    }
}

public interface ILoadMemberScenarioWithBodyAdvancedOperation : ILoadMemberScenarioWithBodyOperation
{
    async Task<OperationResult> ILoadMemberScenarioWithBodyOperation.ExecuteAsync(WorkItem input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Prepare(input);
        await Task.Yield();
        return Finish(normalized);
    }

    private static WorkItem Prepare(WorkItem input)
    {
        return input with { Priority = input.Priority + 200 };
    }

    private static OperationResult Finish(WorkItem input)
    {
        return new OperationResult(true, $"InterfaceAdvanced:{input.Name}:{input.Priority}");
    }
}

public abstract class LoadMemberScenarioOperationBase : ILoadMemberScenarioOperation
{
    public virtual async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(input);
        await DelayAsync(cancellationToken);
        return Complete(normalized);
    }

    protected virtual WorkItem Normalize(WorkItem input)
    {
        return input with { Priority = input.Priority + 1 };
    }

    protected virtual Task DelayAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(1, cancellationToken);
    }

    protected virtual OperationResult Complete(WorkItem input)
    {
        return new OperationResult(true, $"Base:{input.Name}:{input.Priority}");
    }
}

public sealed class LoadMemberScenarioOperation : LoadMemberScenarioOperationBase
{
    public override async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        var adjusted = input with { Priority = input.Priority + 10 };
        return await base.ExecuteAsync(adjusted, cancellationToken);
    }
}

public sealed class LoadMemberScenarioAdvancedOperation : LoadMemberScenarioOperationBase
{
    public override async Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        var adjusted = Normalize(input);
        await DelayAsync(cancellationToken);
        return new OperationResult(true, $"Advanced:{adjusted.Name}:{adjusted.Priority}");
    }

    protected override WorkItem Normalize(WorkItem input)
    {
        return input with { Priority = input.Priority + 20 };
    }
}

public sealed class LoadMemberScenarioWithBodyOperation : ILoadMemberScenarioWithBodyOperation
{
    public Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationResult(true, $"ConcreteInterface:{input.Name}:{input.Priority}"));
    }
}

public sealed class LoadMemberScenarioWithBodyAdvancedOperation : ILoadMemberScenarioWithBodyAdvancedOperation
{
    public Task<OperationResult> ExecuteAsync(WorkItem input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationResult(true, $"ConcreteInterfaceAdvanced:{input.Name}:{input.Priority}"));
    }
}

public static class LoadMemberScenarioEntryPoints
{
    public static Task<OperationResult> RunBasicAsync(CancellationToken cancellationToken = default)
    {
        LoadMemberScenarioOperationBase operation = new LoadMemberScenarioOperation();
        return operation.ExecuteAsync(new WorkItem(Guid.Empty, "basic", 1), cancellationToken);
    }

    public static Task<OperationResult> RunAdvancedAsync(CancellationToken cancellationToken = default)
    {
        LoadMemberScenarioOperationBase operation = new LoadMemberScenarioAdvancedOperation();
        return operation.ExecuteAsync(new WorkItem(Guid.Empty, "advanced", 2), cancellationToken);
    }

    public static Task<OperationResult> RunViaInterfaceAsync(CancellationToken cancellationToken = default)
    {
        ILoadMemberScenarioOperation operation = new LoadMemberScenarioAdvancedOperation();
        return operation.ExecuteAsync(new WorkItem(Guid.Empty, "advanced", 2), cancellationToken);
    }

    public static Task<OperationResult> RunInterfaceBodyBasicAsync(CancellationToken cancellationToken = default)
    {
        ILoadMemberScenarioWithBodyOperation operation = new LoadMemberScenarioWithBodyOperation();
        return operation.ExecuteAsync(new WorkItem(Guid.Empty, "body-basic", 3), cancellationToken);
    }

    public static Task<OperationResult> RunInterfaceBodyAdvancedAsync(CancellationToken cancellationToken = default)
    {
        ILoadMemberScenarioWithBodyOperation operation = new LoadMemberScenarioWithBodyAdvancedOperation();
        return operation.ExecuteAsync(new WorkItem(Guid.Empty, "body-advanced", 4), cancellationToken);
    }
}
