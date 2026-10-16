using System.Collections.Concurrent;
using System.Diagnostics;
namespace OrchDesk.Core.Providers;

public interface IChildProcess : IDisposable { void KillTree(); }

/// <summary>Tracks owned children for operation disposal and host shutdown.</summary>
public sealed class ChildProcessRegistry : IDisposable
{
    private readonly ConcurrentDictionary<Lease, byte> active = new();
    public IDisposable Track(IChildProcess process) {
        var lease=new Lease(this,process); active.TryAdd(lease,0); return lease;
    }
    public void Dispose() { foreach(var lease in active.Keys) lease.Dispose(); }
    private sealed class Lease(ChildProcessRegistry owner,IChildProcess process) : IDisposable {
        private int disposed;
        public void Dispose() {
            if(Interlocked.Exchange(ref disposed,1)!=0) return;
            try { process.KillTree(); } finally { owner.active.TryRemove(this,out _); process.Dispose(); }
        }
    }
}

public static class ChildProcesses
{
    private static readonly ChildProcessRegistry Registry = new();
    static ChildProcesses() { AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll(); }
    public static IDisposable Track(Process process) => Registry.Track(new OwnedProcess(process));
    public static void StopAll() => Registry.Dispose();
    private sealed class OwnedProcess(Process process) : IChildProcess {
        public void KillTree() {
            try { if(!process.HasExited) process.Kill(entireProcessTree:true); }
            catch (InvalidOperationException) {} catch (System.ComponentModel.Win32Exception) {}
        }
        public void Dispose() => process.Dispose();
    }
}
