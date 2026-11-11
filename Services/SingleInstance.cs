using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
namespace ClearNest.Services;
public sealed class SingleInstance : IDisposable
{
 private readonly Mutex mutex;
 private readonly EventWaitHandle? activation;
 private RegisteredWaitHandle? registration;
 private readonly string eventName;
 private bool disposed;
 public bool IsPrimary {get;}
 public SingleInstance()
 {
  using var identity=WindowsIdentity.GetCurrent();
  string key="Local\\ClearNest."+identity.User?.Value+"."+Process.GetCurrentProcess().SessionId;
  eventName=key+".Activate";
  mutex=new Mutex(true,key+".Instance",out bool created);
  IsPrimary=created;
  if(IsPrimary)activation=new EventWaitHandle(false,EventResetMode.AutoReset,eventName);
 }
 public bool NotifyPrimary()
 {
  for(int attempt=0;attempt<40;attempt++)
  {
   try {using var signal=EventWaitHandle.OpenExisting(eventName);return signal.Set();}
   catch(WaitHandleCannotBeOpenedException){Thread.Sleep(50);}
   catch(UnauthorizedAccessException){return false;}
  }
  return false;
 }
 public void Listen(Action activate)
 {
  if(!IsPrimary||activation==null)throw new InvalidOperationException("Only the primary instance may listen.");
  registration=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>activate(),null,Timeout.Infinite,false);
 }
 public void Dispose()
 {
  if(disposed)return;disposed=true;registration?.Unregister(null);activation?.Dispose();
  if(IsPrimary)mutex.ReleaseMutex();
  mutex.Dispose();
 }
}
