namespace COTK.Next.Core;

internal sealed class TransferPause
{
    private readonly object _gate=new();
    private TaskCompletionSource? _resume;
    public bool Paused {get {lock(_gate)return _resume is not null;}}
    public void Pause() {lock(_gate)_resume ??=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);}
    public void Resume() {TaskCompletionSource? resume;lock(_gate){resume=_resume;_resume=null;}resume?.TrySetResult();}
    public async Task WaitAsync(CancellationToken ct) {Task? wait;lock(_gate)wait=_resume?.Task;if(wait is not null)await wait.WaitAsync(ct);ct.ThrowIfCancellationRequested();}
}
