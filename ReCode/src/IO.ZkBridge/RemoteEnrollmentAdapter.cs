using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IO.DeviceContracts;

/// <summary>Exclusive disposable STA session. Never assigns a callback across children or returns template bytes.</summary>
[SupportedOSPlatform("windows")]
internal static class RemoteEnrollmentAdapter
{
    private delegate void EnrollmentEvent(string person,int finger,int action,int length);
    internal static RemoteEnrollOutput Run(RemoteEnrollInput input)
    {
        object? instance=null;bool connected=false,subscribed=false;EnrollmentEvent? callback=null;
        var events=new Guid("cf83b580-5d32-4c65-b44e-bedc750cdfa8");
        RemoteEnrollWorkflow? workflow=null;EnrollmentSession? session=null;
        try
        {
            if(!input.IsValid())return new("unsupported");
            instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00853A19-BD51-419B-9269-2DABE57EB61F"),true)!)!;dynamic sdk=instance;
            if(!(bool)sdk.SetCommPassword(input.Connection.CommKey))return new("failed-before-dispatch");
            connected=(bool)sdk.Connect_Net(input.Connection.Address,input.Connection.Port);
            if(!connected)return new("failed-before-dispatch");
            session=new(instance,input.Connection.MachineNumber,input.Approval);
            var queue=new ConcurrentQueue<RemoteEnrollCompletion>();int received=0;
            callback=(person,finger,action,length)=>
            {
                // Correlation is supplied ONLY inside this single-use exclusive child. Bounded callback queue.
                if(Interlocked.Increment(ref received)<=32)queue.Enqueue(new(input.OperationId,person,finger,action,length));
            };
            ComEventsHelper.Combine(instance,events,18,callback);subscribed=true;
            if(!(bool)sdk.RegEvent(input.Connection.MachineNumber,input.Approval.EventMask))return new("failed-before-dispatch");
            // Drain pre-existing messages before arming. Nothing from another operation is accepted as an id.
            Pump();while(queue.TryDequeue(out _)){}
            workflow=new(input.OperationId,input.PersonIdentifier,input.Approval);
            var state=workflow.Start(session);
            if(state!="awaiting-subject")return new(state);
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed<TimeSpan.FromSeconds(input.SubjectWaitSeconds))
            {
                Pump();
                if(received>32)return new(workflow.Cancel(session));
                while(queue.TryDequeue(out var completion))
                {
                    state=workflow.ObserveCompletion(completion,session);
                    if(state=="completed")return new(state,completion);
                    if(state=="unknown")return new(state);
                }
                Thread.Sleep(10);
            }
            // SDK cancellation is best effort, never cancellation proof. Parent enforces a harder process deadline.
            return new(workflow.Cancel(session));
        }
        catch{return new(workflow?.State is "dispatching" or "awaiting-subject" or "unknown"?"unknown":"failed-before-dispatch");}
        finally
        {
            if(instance is not null)
            {
                if(workflow?.State is "dispatching" or "awaiting-subject")try{workflow.Cancel(session!);}catch{}
                try{if(subscribed&&callback is not null)ComEventsHelper.Remove(instance,events,18,callback);}catch{}
                try{if(connected)((dynamic)instance).Disconnect();}catch{}
                try{if(Marshal.IsComObject(instance))Marshal.FinalReleaseComObject(instance);}catch{}
            }
        }
    }
    private sealed class EnrollmentSession(object instance,int machine,RemoteEnrollApproval approval):IRemoteEnrollSession
    {
        public RemoteEnrollIdentity ReadIdentity()
        {
            dynamic sdk=instance;string serial,product,firmware;
            if(!(bool)sdk.GetSerialNumber(machine,out serial)||!(bool)sdk.GetProductCode(machine,out product)||!(bool)sdk.GetFirmwareVersion(machine,out firmware))throw new InvalidOperationException();
            return new(serial.Trim(),product.Trim(),firmware.Trim());
        }
        public bool? SlotPresent(string person,int finger)
        {
            dynamic sdk=instance;int flag,length;string data;
            // The SDK has no presence-only method. Template material exists transiently inside this child only,
            // is neither emitted nor persisted, and is released with the isolated process. Managed strings cannot promise secure erasure.
            var found=(bool)sdk.GetUserTmpExStr(machine,person,finger,out flag,out data,out length);
            var hasData=!string.IsNullOrEmpty(data);data=null!;
            if(found)return length>0&&hasData?true:null;
            if(length!=0||hasData)return null;
            int error=int.MinValue;sdk.GetLastError(ref error);
            // Absence is NEVER inferred from a generic false. Exact installed-model error semantics are deployment-approved.
            return approval.EmptySlotErrorCode is{} absent&&absent!=0&&absent!=int.MinValue&&error==absent?false:null;
        }
        public bool StartEnrollEx(string person,int fingerIndex,int flag)=>(bool)((dynamic)instance).StartEnrollEx(person,fingerIndex,flag);
        public bool CancelOperation()=>(bool)((dynamic)instance).CancelOperation();
    }
    [StructLayout(LayoutKind.Sequential)]private struct Message{public IntPtr Window;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool PeekMessage(out Message message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]private static extern IntPtr DispatchMessage(ref Message message);
    private static void Pump(){int budget=256;while(budget-->0&&PeekMessage(out var message,IntPtr.Zero,0,0,1)){TranslateMessage(ref message);DispatchMessage(ref message);}}
}
