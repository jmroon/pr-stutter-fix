using System;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PRStutter.UnroundedExperiment;

static class Program
{
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static void Throws(Action action, string reason) { try { action(); } catch { return; } throw new Exception(reason); }
    static void Main(string[] args)
    {
        foreach (string game in new[] { "FFIV", "FFVI" }) {
            var sites = PatchPlan.For(game);
            var memory = new FakeMemory(sites); var patches = new PatchSet(memory, sites);
            var original = memory.Bytes.ToArray();
            patches.Set(true); Check(patches.Enabled, "Enable failed");
            patches.Set(true); patches.Set(false); Check(memory.Bytes.SequenceEqual(original),"Toggle did not restore exact image");
            patches.Set(true); patches.Restore(); Check(!patches.HasOwnedSites && memory.Bytes.SequenceEqual(original),"Unload restoration");

            memory = new FakeMemory(sites); patches = new PatchSet(memory, sites);
            memory.Bytes[sites[^1].Rva] = 0xCC;
            Throws(() => patches.Set(true),"Foreign call accepted");
            Check(memory.Writes==0 && patches.Faulted,"Preflight partially wrote before refusal");

            memory = new FakeMemory(sites); patches = new PatchSet(memory, sites);
            original = memory.Bytes.ToArray(); memory.FailWrite = 2;
            Throws(() => patches.Set(true),"Injected partial write ignored");
            Check(patches.Faulted && !patches.HasOwnedSites && memory.Bytes.SequenceEqual(original),"Partial activation not rolled back");
            Throws(() => patches.Set(true),"Fault automatically retried");

            memory = new FakeMemory(sites); patches = new PatchSet(memory, sites);
            original = memory.Bytes.ToArray(); patches.Set(true); memory.FailWrite = memory.Writes + 2;
            Throws(() => patches.Set(false),"Injected deactivation failure ignored");
            Check(!patches.HasOwnedSites && memory.Bytes.SequenceEqual(original),"Partial deactivation not restored");

            memory = new FakeMemory(sites); memory.Bytes[sites[0].Rva-1]=0xCC;
            Throws(() => new PatchSet(memory,sites),"Altered surrounding instruction accepted");

            memory = new FakeMemory(sites); patches = new PatchSet(memory, sites); patches.Set(true);
            memory.Bytes[sites[0].Rva] = 0xCC;
            Throws(patches.Restore,"Foreign patch overwritten during restore");
            Check(memory.Bytes[sites[0].Rva]==0xCC && patches.HasOwnedSites && patches.Faulted,"Ownership conflict lost");
            Check(memory.Read(sites[1].Rva,5).SequenceEqual(sites[1].Original),"Conflict blocked other restoration");

            memory = new FakeMemory(sites); patches = new PatchSet(memory, sites);
            Task.Run(() => Throws(() => patches.Set(true),"Foreign-thread patch accepted")).GetAwaiter().GetResult();
            Check(memory.Writes==0,"Foreign thread mutated code");
        }
        Console.WriteLine("PASS: all-site preflight, exact restoration, partial-write rollback, fault latch, foreign ownership and thread refusal.");

        string profile=args[0]; using var stream=File.OpenRead(args[1]); using var pe=new PEReader(stream);
        foreach(var s in PatchPlan.For(profile)) {
            var actual=pe.GetSectionData(s.Rva-5).GetContent(0,15).ToArray();
            Check(actual.SequenceEqual(Convert.FromHexString(s.Before+s.Call+s.After)),"Disk instruction fingerprint: "+s.Name);
            int target=s.Rva+5+BitConverter.ToInt32(s.Original,1);
            Check(target==(profile=="FFIV"?0xDE1B0:0xE86D0),"Unexpected rounding helper");
        }
        Console.WriteLine("PASS: exact installed PE call-site fingerprints and shared rounding-helper destinations ("+profile+").");
        NativeRoundTrip();
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float Unary(float value);
    private static void NativeRoundTrip()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new NotSupportedException("Windows x64 integration check required");
        // Isolated executable memory, never the game: wrapper calls an add-one
        // helper. Bypassing the CALL must return the original float in XMM0.
        var buffer = Enumerable.Repeat((byte)0x90,4096).ToArray();
        byte[] wrapper={0x48,0x83,0xEC,0x28,0xE8,39,0,0,0,0x48,0x83,0xC4,0x28,0xC3};
        Array.Copy(wrapper,0,buffer,16,wrapper.Length);
        byte[] helper={0xF3,0x0F,0x58,0x05,24,0,0,0,0xC3}; Array.Copy(helper,0,buffer,64,helper.Length);
        Array.Copy(BitConverter.GetBytes(1f),0,buffer,96,4);
        var ptr=VirtualAlloc(IntPtr.Zero,(UIntPtr)4096,0x3000,0x04); Check(ptr!=IntPtr.Zero,"VirtualAlloc");
        try {
            Marshal.Copy(buffer,0,ptr,buffer.Length);
            Check(VirtualProtect(ptr,(UIntPtr)4096,0x20,out _),"Initial RX protection");
            Check(FlushInstructionCache(GetCurrentProcess(),ptr,(UIntPtr)4096),"Initial instruction flush");
            var invoke=Marshal.GetDelegateForFunctionPointer<Unary>(IntPtr.Add(ptr,16));
            var site=new CallSite(20,"synthetic",Convert.ToHexString(buffer[15..20]),Convert.ToHexString(buffer[20..25]),Convert.ToHexString(buffer[25..30]));
            var patches=new PatchSet(new ProcessCodeMemory(ptr,4096),new[]{site});
            foreach(float value in new[]{-10.25f,0f,.25f,100.125f}) {
                Check(invoke(value)==value+1,"Original CALL ABI");
                patches.Set(true); Check(invoke(value)==value,"Bypass did not preserve XMM0");
                patches.Set(false); Check(invoke(value)==value+1,"Restored CALL failed");
            }
            patches.Restore();
            Console.WriteLine("PASS: actual Windows x64 executable-memory CALL bypass/restore preserves fractional float arguments; no game process used.");
        } finally { Check(VirtualFree(ptr,UIntPtr.Zero,0x8000),"VirtualFree"); }
    }
    [DllImport("kernel32",SetLastError=true)] static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protection);
    [DllImport("kernel32",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool VirtualFree(IntPtr address,UIntPtr size,uint type);
    [DllImport("kernel32",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protection,out uint previous);
    [DllImport("kernel32",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
    [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
}
internal sealed class FakeMemory : ICodeMemory
{
    public byte[] Bytes;
    public int Writes, FailWrite;
    public FakeMemory(CallSite[] sites) {
        Bytes=new byte[sites.Max(s=>s.Rva)+16];
        foreach(var s in sites) Array.Copy(Convert.FromHexString(s.Before+s.Call+s.After),0,Bytes,s.Rva-5,15);
    }
    public byte[] Read(int rva,int length)=>Bytes[rva..(rva+length)];
    public void Write(int rva,byte[] bytes) {
        Array.Copy(bytes,0,Bytes,rva,bytes.Length); Writes++;
        if(Writes==FailWrite)throw new IOException("Injected write-then-fail");
    }
}
