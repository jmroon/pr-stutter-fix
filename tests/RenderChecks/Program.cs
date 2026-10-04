using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.RenderExperiment;
using PRStutter.GridExperiment;

static void Check(bool value, string why) { if (!value) throw new Exception(why); }
// Exercise both possible LateUpdate orderings using the actual callback gate.
foreach (bool activateAfterRenderDriver in new[] { false, true }) {
    var gate = new RenderFrameGate();
    if (activateAfterRenderDriver) {
        Check(!gate.AcceptCallback(10) && !gate.AcceptCallback(10), "Startup must ignore both pre/post callbacks before first preparation");
        gate.Restored(); // Next Update's recovery must not arm the session.
        Check(!gate.AcceptCallback(11), "Restoration unexpectedly armed rendering");
    }
    gate.Prepared(11);
    Check(gate.AcceptCallback(11), "Prepared frame rejected");
    gate.Restored();
    bool refused = false;
    try { gate.AcceptCallback(11); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "Extra callbacks after restoration silently accepted");
    gate.Prepared(12); Check(gate.AcceptCallback(12), "Next prepared frame rejected");
    refused = false;
    try { gate.AcceptCallback(13); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "Missing later preparation silently accepted");
}
Console.WriteLine("PASS: automatic activation before/after render-driver LateUpdate, first-frame stock fallback, and strict callbacks after arming.");
var ffivCameras = new[] { "CameraFieldMain", "CameraTileMap" };
var ffviCameras = new[] { "CameraFieldMain", "CameraTileMap", "CameraUpperTransparentRT", "CameraCeilTransparentRT" };
Check(FieldLayoutPolicy.Accepts("FFIV", ffivCameras, 1), "Recorded FFIV shared-target layout rejected");
Check(FieldLayoutPolicy.Accepts("FFVI", ffviCameras, 3), "Inspected FFVI layout rejected");
Check(!FieldLayoutPolicy.Accepts("FFVI", ffivCameras, 1), "FFVI silently accepted an incomplete layout");
Check(!FieldLayoutPolicy.Accepts("FFIV", ffviCameras, 3), "FFIV silently accepted an uninspected layout");
Check(!FieldLayoutPolicy.Accepts("FFIV", ffivCameras, 2), "Split FFIV targets accepted");
Check(!FieldLayoutPolicy.Accepts("FFIV", new[] { "CameraFieldMain", "CameraTileMap", "CameraTileMap" }, 1), "Duplicate camera accepted");
Check(!FieldLayoutPolicy.Accepts("FFIV", new[] { "CameraUpperTransparentRT", "CameraCeilTransparentRT" }, 1), "Unrelated camera pair accepted");
Check(!FieldLayoutPolicy.Accepts("FFI", ffivCameras, 1), "Unknown game accepted");
float singleRoot = 10;
var singleCamera = new InheritedTranslation<float>(() => singleRoot, value => singleRoot = value,
    Array.Empty<Func<float>>(), (a,b) => a+b, (a,b) => a==b);
foreach (float offset in new[] { -.5f, 0, .25f }) {
    singleCamera.Apply(offset); singleCamera.Validate();
    Check(singleRoot == 10 + offset, "FFIV single render camera translation failed");
    singleCamera.Restore(); Check(singleRoot == 10, "FFIV single render camera restoration failed");
}
Console.WriteLine("PASS: distinct FFIV/FFVI named layouts, rejection of incomplete/unknown layouts, and single-root translation/restoration.");
// Regression: the transparency cameras inherit the tile camera's transform.
// Exercise the same root-only translation object used by the plugin, with live
// descendant getters rather than independent camera positions.
float cameraRoot = 10, upperLocal = 0, ceilingLocal = -2;
bool failCameraAfterWrite = false;
var inherited = new InheritedTranslation<float>(() => cameraRoot, value => {
    cameraRoot = value;
    if (failCameraAfterWrite) throw new InvalidOperationException("partial root write");
}, new Func<float>[] { () => cameraRoot + upperLocal, () => cameraRoot + ceilingLocal },
    (a, b) => a + b, (a, b) => a == b);
foreach (float offset in new[] { .25f, -.5f, 0f, .5f, -.25f }) {
    inherited.Apply(offset); inherited.Validate();
    Check(cameraRoot == 10 + offset && cameraRoot + upperLocal == 10 + offset && cameraRoot + ceilingLocal == 8 + offset,
        "Parent/child cameras did not receive exactly one offset");
    inherited.Restore(); inherited.Restore();
    Check(cameraRoot == 10 && upperLocal == 0 && ceilingLocal == -2, "Hierarchy restoration changed child-local positions");
}
failCameraAfterWrite = true;
try { inherited.Apply(.25f); throw new Exception("Expected partial root setter failure"); } catch (InvalidOperationException) { }
failCameraAfterWrite = false; inherited.Restore();
Check(cameraRoot == 10 && upperLocal == 0 && ceilingLocal == -2, "Failed hierarchy application left a camera offset");
inherited.Apply(.5f); upperLocal = .25f;
try { inherited.Validate(); throw new Exception("Expected child readback failure"); } catch (InvalidOperationException) { }
inherited.Restore();
Check(cameraRoot == 10 && upperLocal == .25f, "Root restoration overwrote an independent child edit");
upperLocal = 0;
inherited.Apply(.25f); cameraRoot = 12; inherited.Restore();
Check(cameraRoot == 12 && upperLocal == 0 && ceilingLocal == -2, "Root restoration overwrote an independent root edit");
Console.WriteLine("PASS: actual hierarchy-translation helper offsets parent/child cameras once, restores without changing child-local positions, and handles partial failure and independent edits.");
int ownedValue = 7;
bool failBefore = false, failAfter = false;
var scoped = new ScopedOverride<int>(() => ownedValue, value => {
    if (failBefore) throw new InvalidOperationException("before write");
    ownedValue = value;
    if (failAfter) throw new InvalidOperationException("after write");
});
scoped.Apply(9); scoped.Restore(); scoped.Restore();
Check(ownedValue == 7 && !scoped.Pending, "Scoped restoration failed");
scoped.Apply(9); ownedValue = 11; scoped.Restore();
Check(ownedValue == 11, "Scoped restore overwrote another writer");
ownedValue = 7; failBefore = true;
try { scoped.Apply(9); throw new Exception("Expected failed setter"); } catch (InvalidOperationException) { }
failBefore = false; scoped.Restore();
Check(ownedValue == 7 && !scoped.Pending, "Pre-write failure restoration failed");
failAfter = true;
try { scoped.Apply(9); throw new Exception("Expected partial setter failure"); } catch (InvalidOperationException) { }
failAfter = false; scoped.Restore();
Check(ownedValue == 7, "Post-write failure restoration failed");
scoped.Apply(9); failBefore = true;
try { scoped.Restore(); throw new Exception("Expected restore failure"); } catch (InvalidOperationException) { }
Check(scoped.Pending, "Failed restore lost ownership");
failBefore = false; scoped.Restore();
Check(ownedValue == 7 && !scoped.Pending, "Restore retry failed");
scoped.Apply(8); scoped.Apply(9); scoped.Restore();
Check(ownedValue == 7, "Reapply lost original state");
// Raster-space model: body/head/shadow anchors on the source-pixel grid,
// integer high-res texel compensation followed by the opposite image translation.
foreach (int scale in new[] { 2, 4, 8 })
foreach (int fps in new[] { 30, 60, 120, 144, 165, 240, 360 })
for (int direction = -1; direction <= 1; direction += 2)
for (int step = 0; step < fps * 2; step++)
{
    float candidate = direction * 80 * (step / (float)fps);
    float rounded = (float)Math.Round(candidate), residual = candidate - rounded;
    float correction = StabilizationMath.Quantize(residual, scale);
    Check(Math.Abs(correction - residual) <= .5f / scale + .00001f, "Correction error exceeds half a high-res texel");
    float samplingShift = correction / 320 * (320 * scale);
    foreach (float anchor in new[] { 0f, -16f, -4.5f }) {
        double before = Math.Round(anchor * scale);
        double correctedRaster = Math.Round((anchor + correction) * scale);
        Check(Math.Abs(correctedRaster - samplingShift - before) < .0001, "Player drifts after rasterization/compositing");
    }
}
try { StabilizationMath.Quantize(float.NaN, 8); throw new Exception("Nonfinite residual accepted"); } catch (ArgumentOutOfRangeException) { }
Console.WriteLine("PASS: scoped render-state rollback, partial setter failures, retry and independent writes; high-res player cancellation at 30/60/120/144/165/240/360 fps in the raster model (not an in-game frame-rate test).");
// New pipeline: camera offset participates in rasterization, not a later image shift.
// Static scenery and a static NPC must translate together; followed player stays fixed.
foreach (int gridScale in new[] { 4, 8 }) {
double worstStockStepError = 0, worstGridStepError = 0, previousStock = 0, previousGrid = 0;
for (int frame = 0; frame <= 120; frame++) {
    float continuous = 80 * frame / 60f;
    float logical = (float)Math.Round(continuous);
    float correction = StabilizationMath.Quantize(continuous - logical, gridScale);
    double presented = logical + correction;
    double backgroundPixel = Math.Round((128 - logical - correction) * gridScale);
    double staticNpcPixel = Math.Round((160 - logical - correction) * gridScale);
    Check(staticNpcPixel - backgroundPixel == 32 * gridScale, "Static NPC slips relative to scenery");
    foreach (float anchor in new[] { 0f, -16f, -4.5f }) {
        double playerPixel = Math.Round((anchor + correction - correction) * gridScale);
        Check(playerPixel == Math.Round(anchor * gridScale), "Pre-raster camera/player offsets wobble");
    }
    Check(Math.Abs(presented - continuous) <= .5 / gridScale + .00001, "Render-grid position error exceeds half a texel");
    if (frame > 0) {
        worstStockStepError = Math.Max(worstStockStepError, Math.Abs(logical - previousStock - 80.0 / 60));
        worstGridStepError = Math.Max(worstGridStepError, Math.Abs(presented - previousGrid - 80.0 / 60));
    }
    previousStock = logical; previousGrid = presented;
}
Check(worstGridStepError <= worstStockStepError / gridScale + .00001, "Finer render grid did not reduce modeled cadence error");
Console.WriteLine($"PASS: {gridScale}x pre-raster camera/player cancellation, static-NPC/scenery alignment, <={.5 / gridScale:F5}-unit position error; modeled 60 Hz walk step error {worstStockStepError:F4} -> {worstGridStepError:F4} units. Not a live visual result.");
}
float[] initial = { 0, 1, 0, 1 }, values = (float[])initial.Clone();
int failAt = -1;
var uv = new UvOverride(initial, i => values[i], (i, value) =>
{
    if (i == failAt) throw new InvalidOperationException("Simulated material write failure");
    values[i] = value;
});
Check(uv.Apply(.001f, -.002f), "UV apply rejected");
Check(values.SequenceEqual(new[] { .001f, 1.001f, -.002f, .998f }), "Incorrect UV offsets");
uv.Restore(); uv.Restore();
Check(values.SequenceEqual(initial), "UV restore/idempotence failed");
Check(uv.Apply(.001f, -.002f), "Second apply rejected");
values[0] = .25f; // A later independent game write must survive restoration.
uv.Restore();
Check(values.SequenceEqual(new[] { .25f, 1f, 0f, 1f }), "Restore overwrote an independent update");
Check(!uv.Apply(.001f, 0), "Changed game UVs accepted");
values[0] = float.NaN;
Check(!uv.Apply(0, 0), "Nonfinite game UV accepted");
values[0] = 0;
Check(!uv.Apply(float.NaN, 0), "Nonfinite correction accepted");
failAt = 2;
try { uv.Apply(.001f, -.002f); throw new Exception("Write failure did not propagate"); }
catch (InvalidOperationException) { }
failAt = -1; uv.Restore();
Check(values.SequenceEqual(initial), "Partial apply did not restore");
uv.Apply(.001f, -.002f); failAt = 0;
try { uv.Restore(); throw new Exception("Restore failure did not propagate"); }
catch (InvalidOperationException) { }
Check(values[1] == 1 && values[2] == 0 && values[3] == 1, "Restore failure prevented other properties recovering");
failAt = -1; uv.Restore();
Check(values.SequenceEqual(initial), "Pending restore retry failed");
Console.WriteLine("PASS: UV offsets, restoration, partial failures, retry, independent writes and nonfinite guards.");
for (int direction = -1; direction <= 1; direction += 2)
for (int axis = 0; axis < 2; axis++)
for (int frame = 0; frame <= 120; frame++)
{
    float timer = frame / 600f, candidate = direction * 16 * (timer / .2f), rounded = (float)Math.Round(candidate);
    bool ok = MotionResidual.TryCalculate(0, 0, axis == 0 ? direction * 16 : 0, axis == 1 ? direction * 16 : 0,
        axis == 0 ? rounded : 0, axis == 1 ? rounded : 0, timer, .2f, out float x, out float y);
    Check(ok, "Ordinary motion rejected");
    float residual = axis == 0 ? x : y;
    Check(Math.Abs(residual) <= .5001f && Math.Abs(rounded + residual - candidate) < .0001f, "Correction is unbounded or does not reconstruct motion");
    Check(frame != 120 || residual == 0, "Correction must return to zero at destination");
}
foreach (int dirX in new[] { -1, 1 })
foreach (int dirY in new[] { -1, 1 })
foreach (int scale in new[] { 4, 8 })
for (int frame = 0; frame <= 240; frame++) {
    float duration = .2f * MathF.Sqrt(2), timer = duration * (frame / 240f);
    float cx = 64 + dirX * 16 * (timer / duration), cy = -184 + dirY * 16 * (timer / duration);
    float px = (float)Math.Round(cx), py = (float)Math.Round(cy);
    Check(MotionResidual.TryCalculate(64, -184, 64 + dirX * 16, -184 + dirY * 16, px, py,
        timer, duration, out float rx, out float ry), "Diagonal residual rejected");
    foreach (var axis in new[] { (px, cx, rx), (py, cy, ry) }) {
        float correction = StabilizationMath.Quantize(axis.Item3, scale);
        Check(Math.Abs(axis.Item1 + axis.Item3 - axis.Item2) < .0001f, "Diagonal residual failed to reconstruct motion");
        Check(Math.Abs(correction - axis.Item3) <= .5f / scale + .00001f, "Diagonal quantization exceeds half texel per axis");
        Check(frame != 240 || correction == 0, "Diagonal endpoint correction is not zero");
    }
}
Check(MotionResidual.TryCalculate(64,-184,80,-200,64,-184,.0084416f,.2828427f,out _,out _), "Captured diagonal residual rejected");
Check(!MotionResidual.TryCalculate(0,0,16,8,0,0,0,.2f,out _,out _), "Unequal non-tile diagonal accepted");
Check(!MotionResidual.TryCalculate(0,0,16,16,0,99,0,.2828427f,out _,out _), "Mismatched diagonal Y accepted");
Console.WriteLine("PASS: four diagonal directions at 4x/8x reconstruct both axes, bound correction to half a texel per axis and return to zero at endpoints; captured diagonal supported.");
Check(!MotionResidual.TryCalculate(0,0,16,0,99,0,.02f,.2f,out _,out _), "Teleport/mismatched position accepted");
Check(!MotionResidual.TryCalculate(0,0,16,0,0,0,float.NaN,.2f,out _,out _), "NaN accepted");
Check(!MotionResidual.TryCalculate(0,0,16,0,0,0,.3f,.2f,out _,out _), "Expired timer accepted");
Check(!MotionResidual.TryCalculate(0,0,160,0,0,0,.01f,.2f,out _,out _), "Non-walk path accepted");
Check(MotionResidual.TryCalculate(0,0,16,0,16,0,0,0,out float idleX,out float idleY) && idleX == 0 && idleY == 0, "Idle correction must be zero");
// Replay the user's preserved good capture, when provided, using the same policy as the plugin.
if (args.Length > 1)
{
    string[] lines = File.ReadAllLines(args[1]), header = lines[0].Split(',');
    int accepted = 0, moving = 0;
    foreach (string line in lines.Skip(1))
    {
        string[] row = line.Split(',');
        float F(string key) => float.Parse(row[Array.IndexOf(header, key)], CultureInfo.InvariantCulture);
        if (F("move_duration") <= 0) continue;
        moving++;
        if (MotionResidual.TryCalculate(F("start_x"), F("start_y"), F("dest_x"), F("dest_y"), F("local_x"), F("local_y"),
            F("move_timer"), F("move_duration"), out _, out _)) accepted++;
    }
    Check(moving > 0 && moving == accepted, "Captured ordinary motion failed the strict correction guard");
    Console.WriteLine($"PASS: replayed {accepted} captured moving samples within the half-unit correction bound.");
}
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
bool nativeMode = reader.GetString(reader.GetAssemblyDefinition().Name) == "PRStutter.NativeScrollExperiment";
bool gridMode = reader.GetString(reader.GetAssemblyDefinition().Name) == "PRStutter.GridExperiment";
bool sawNativeScroll = false;
foreach (var handle in reader.AssemblyReferences)
{
    string name = reader.GetString(reader.GetAssemblyReference(handle).Name);
    Check(!name.Contains("Harmony") && !name.Contains("MonoMod"), "Native patching reference present");
}
foreach (var handle in reader.MethodDefinitions)
    Check((reader.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) == 0, "Native import present");
foreach (var handle in reader.MemberReferences)
{
    var member = reader.GetMemberReference(handle);
    if (member.Parent.Kind != HandleKind.TypeReference) continue;
    var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
    string ns = reader.GetString(type.Namespace), name = reader.GetString(member.Name), typeName = reader.GetString(type.Name);
    if (ns.StartsWith("UnityEngine") || ns.StartsWith("Last."))
    {
        bool renderSetter = !nativeMode && ns == "UnityEngine" && (typeName, name) is
            ("Camera", "set_targetTexture") or
            ("RenderTexture", "set_width") or ("RenderTexture", "set_height") or
            ("Texture", "set_width") or ("Texture", "set_height") or
            ("Object", "set_name") or ("Texture", "set_filterMode") or ("Texture", "set_wrapMode");
        if (!nativeMode && !gridMode && ns == "UnityEngine" && typeName == "Transform" && name == "set_localPosition") renderSetter = true;
        if (gridMode && ns == "UnityEngine" && typeName == "Transform" && name is "set_position" or "set_localPosition") renderSetter = true;
        if (gridMode && ns == "UnityEngine" && typeName == "QualitySettings" && name == "set_vSyncCount") renderSetter = true;
        if (gridMode && ns == "UnityEngine" && typeName == "RenderTexture" && name == "set_active") renderSetter = true;
        Check((!name.StartsWith("set_") || renderSetter) && name is not ("MoveTo" or "UpdateController" or "UpdateMovingSetPosition" or "SetPosition"),
            "Unreviewed game/Unity state mutation reference: " + typeName + "." + name);
        if (nativeMode) {
            Check(name is not ("SetTexture" or "SetFloat" or "SetVector" or "SetMatrix" or "SetOffsetPosition" or "Blit" or "SetParent" or "SetPositionAndRotation"),
                "Native-scroll test must not modify the compositor, camera or transform: " + typeName + "." + name);
            Check(typeName != "RenderTexture" || name != ".ctor", "Native-scroll test must not create render targets");
            if (name == "UpdateMapScrollIfNeed") sawNativeScroll = true;
        }
        if (gridMode) {
            Check(name is not ("UpdateMapScrollIfNeed" or "SetVector" or "SetMatrix" or "SetOffsetPosition" or "Blit" or "SetParent" or "SetPositionAndRotation"),
                "Grid test must not use native map scrolling or image reprojection: " + typeName + "." + name);
            Check(name != "SetFloat", "CRT-off grid test must not write compositor floats/UV bounds");
            Check(!ns.StartsWith("Last.") || name.StartsWith("get_") || name == "GetMaterial",
                "Grid test has an unreviewed game call: " + typeName + "." + name);
        }
    }
}
if (nativeMode) {
    Check(sawNativeScroll, "Expected native map-scroll entry point missing");
    Console.WriteLine("PASS: native-scroll assembly uses the intended map entry point; no native patches/PInvoke, Unity/game setters, compositor setters, new render targets or gameplay movement calls. Actual native rendering still needs live testing.");
} else if (gridMode) Console.WriteLine("PASS: grid assembly has no native patches/PInvoke, game writes/calls, targetFrameRate writes, native scrolling, compositor scalar/UV writes or blits. Reviewed render transforms, scoped vSyncCount/RenderTexture.active writes and temporary CPU texture readback require live verification.");
else Console.WriteLine("PASS: motion guards; no native patches, P/Invoke or game property setters. Unity setters are restricted to the reviewed render-target/visual-transform allowlist. Actual rendering still needs live testing.");
