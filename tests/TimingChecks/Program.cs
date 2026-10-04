using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.TimingExperiment;

static void Check(bool pass, string reason) { if (!pass) throw new Exception(reason); }
static Walk Moving(float start, float timer, int sign = 1, bool vertical = false)
{
    float dest = start + 16 * sign;
    float pos = CarryPolicy.Rounded(start, dest, timer, .2f);
    return vertical ? new Walk(0,start,0,dest,timer,.2f,0,pos) : new Walk(start,0,dest,0,timer,.2f,pos,0);
}
int simulated = 0;
// Compare integrated continuous distance with elapsed time, including jitter and many
// boundaries. The native simulation still invokes completion once per completed tile.
foreach (int fps in new[] { 30, 60, 120, 144, 165, 240, 360 })
foreach (int sign in new[] { -1, 1 })
foreach (bool vertical in new[] { false, true })
foreach (bool jitter in new[] { false, true })
{
    float start = 0, timer = 0;
    double elapsed = 0;
    int callbacks = 0;
    for (int frame = 0; frame < fps * 10; frame++) {
        float dt = (1f / fps) * (jitter ? new[] { .72f, 1.28f, .96f, 1.04f }[frame % 4] : 1);
        var before = Moving(start, timer, sign, vertical);
        bool carry = CarryPolicy.TryRemainder(before, dt, out float remainder);
        timer += dt; elapsed += dt;
        if (timer >= .2f) {
            callbacks++;
            var after = before with { Timer = 0, Duration = 0, X = before.Dx, Y = before.Dy };
            Check(CarryPolicy.Completed(before, after), "Native completion rejected");
            start += 16 * sign;
            var next = Moving(start, 0, sign, vertical);
            Check(CarryPolicy.ApprovedNext(before, next), "Valid next tile rejected");
            timer = carry ? remainder : 0;
        }
        double distance = start + sign * 16 * timer / .2f;
        Check(Math.Abs(distance - sign * 80 * elapsed) < .001, "Continuous walk lost frame time at a boundary");
        Check(Math.Abs(callbacks - Math.Floor(elapsed / .2)) <= 1, "Completion callback count diverged");
        simulated++;
    }
}
Console.WriteLine($"PASS: {simulated} simulated frames, both axes/directions, steady/jittered 30-360 FPS; cumulative motion conserved within .001 game units, completion counts preserved.");

var sample = Moving(64, .199f);
Check(CarryPolicy.TryRemainder(sample,.008f,out float leftover) && Math.Abs(leftover-.007f)<.000001, "Measured shortfall not recovered");
Check(!CarryPolicy.TryRemainder(sample,.1f,out _), "Long stall accepted");
Check(!CarryPolicy.TryRemainder(sample,float.NaN,out _), "NaN delta accepted");
Check(!CarryPolicy.TryRemainder(sample,0,out _), "Paused delta accepted");
Check(!CarryPolicy.TryRemainder(sample with { Duration=.1f },.008f,out _), "Dash accepted");
Check(!CarryPolicy.TryRemainder(sample with { Dy=16 },.008f,out _), "Diagonal with cardinal duration accepted");
Check(!CarryPolicy.TryRemainder(sample with { X=900 },.008f,out _), "Teleport accepted");
Check(!CarryPolicy.TryRemainder(sample with { Timer=.21f },.008f,out _), "Expired timer accepted");
Check(!CarryPolicy.TryRemainder(sample with { Dx=float.NaN },.008f,out _), "NaN endpoint accepted");
Check(CarryPolicy.SameInput(sample,1,0,100,100), "Fresh direction rejected");
Check(!CarryPolicy.SameInput(sample,1,0,99,100), "Stale input accepted");
Check(!CarryPolicy.SameInput(sample,0,0,100,100), "Released input accepted");
Check(!CarryPolicy.SameInput(sample,-1,0,100,100), "Reversal accepted");
Check(!CarryPolicy.SameInput(sample,0,1,100,100), "Turn accepted");
Check(!CarryPolicy.SameInput(sample,1,1,100,100), "Diagonal input accepted");
var endpoint = sample with { Timer=0, Duration=0, X=80, Y=0 };
Check(CarryPolicy.Completed(sample, endpoint), "Endpoint rejected");
Check(!CarryPolicy.Completed(sample, endpoint with { X=96 }), "Callback teleport accepted");
var approved = Moving(80,0);
Check(CarryPolicy.ApprovedNext(sample, approved), "Approved continuation rejected");
Check(!CarryPolicy.ApprovedNext(sample, approved with { Duration=0, Dx=80 }), "Blocked next tile accepted");
Check(!CarryPolicy.ApprovedNext(sample, Moving(80,0,-1)), "Reversing next tile accepted");
Check(!CarryPolicy.ApprovedNext(sample, approved with { Duration=.1f }), "Changed speed accepted");
Check(!CarryPolicy.ApprovedNext(sample, Moving(96,0)), "Skipped tile accepted");
Check(!CarryPolicy.ApprovedNext(sample, Moving(80,.01f)), "Already updated next tile accepted");
Console.WriteLine("PASS: stops, stale input, turns, reversals, collisions, speed/path changes, callback teleports, long frames and nonfinite data rejected.");
Check(CarryPolicy.ReadyBeforeCamera(100,100,99,100,true,true), "Approved same-frame window rejected");
Check(!CarryPolicy.ReadyBeforeCamera(99,100,99,100,true,true), "Old frame budget reused");
Check(!CarryPolicy.ReadyBeforeCamera(100,100,100,100,true,true), "Carry after camera accepted");
Check(!CarryPolicy.ReadyBeforeCamera(100,100,99,99,true,true), "Old foot approval accepted");
Check(!CarryPolicy.ReadyBeforeCamera(100,100,99,100,false,true), "Rejected foot result accepted");
Check(!CarryPolicy.ReadyBeforeCamera(100,100,99,100,true,false), "Closed operation gate bypassed");
Console.WriteLine("PASS: deferred carry requires fresh approved arrival checks, open operation gate, same completion frame and a camera update still ahead.");
ArrivalAdmissionChecks.Run();
CombinedRunChecks.Run();
DiagonalChecks.Run();
ObserverChecks.Run();
AutomaticChecks.Run();

// A separate gameplay experiment has a separate, explicit mutation surface.
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
bool timerSetter=false, positionSetter=false, inputRequest=false, arrivalStart=false, arrivalUpdate=false;
foreach (var h in reader.MethodDefinitions)
    Check((reader.GetMethodDefinition(h).Attributes & MethodAttributes.PinvokeImpl)==0,"Unreviewed native import");
foreach (var h in reader.MemberReferences) {
    var m=reader.GetMemberReference(h);
    if (m.Parent.Kind!=HandleKind.TypeReference) continue;
    var t=reader.GetTypeReference((TypeReferenceHandle)m.Parent);
    string ns=reader.GetString(t.Namespace), type=reader.GetString(t.Name), name=reader.GetString(m.Name);
    if (!ns.StartsWith("UnityEngine") && !ns.StartsWith("Last.")) continue;
    bool timer=type=="FieldEntity" && name=="set_moveTimer";
    bool position=type=="Transform" && name=="set_localPosition";
    bool panelStyle = ns == "UnityEngine" && type == "GUIStyle" && name is "set_alignment" or "set_wordWrap" or "set_padding" or "set_fontSize";
    panelStyle |= ns == "UnityEngine" && ((type == "GUIStyleState" && name == "set_textColor") || (type == "GUIContent" && name == "set_text"));
    Check(!name.StartsWith("set_") || timer || position || panelStyle, "Unexpected game setter: "+type+"."+name);
    Check(name is not ("MoveTo" or "UpdateEntity" or "UpdateController" or "SquareMoveFinished" or "UpdateMovingSetPosition"), "Unexpected explicit gameplay call: "+type+"."+name);
    Check(type != "TaskMachine" || name.StartsWith("get_"), "Whole scheduler mutation or replay: " + name);
    if (ns == "Last.Management" && name is "Start" or "Update") {
        Check(type is "TaskBase" or "EnumeratorTaskProcess", "Unexpected task execution: " + type + "." + name);
        arrivalStart |= name == "Start"; arrivalUpdate |= name == "Update";
    }
    timerSetter|=timer; positionSetter|=position; inputRequest|=type=="FieldPlayerController" && name=="OnTouchPadCallback";
}
Check(timerSetter && positionSetter && inputRequest,"Missing expected narrow carry intervention");
Check(arrivalStart && arrivalUpdate, "Missing single-task start/step");
Console.WriteLine("PASS: compiled game setters limited to moveTimer/localPosition plus local GUI style/content; normal input continuation and explicit task start/step present; no whole-scheduler replay, entity-update replay, MoveTo bypass, camera/frame-rate setter or native imports.");
