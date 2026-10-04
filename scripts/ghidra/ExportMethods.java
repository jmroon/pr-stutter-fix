// Targeted native decompilation using Il2CppDumper RVAs. Never modifies the source DLL.
// @category PRStutter
import com.google.gson.*;
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.SourceType;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.io.*;
import java.util.*;
import java.util.regex.Pattern;

public class ExportMethods extends GhidraScript {
    private final Map<Long, List<JsonObject>> methods = new LinkedHashMap<>();

    private String symbol(String name) {
        return name.replaceAll("[^a-zA-Z0-9_]", "_");
    }

    private Function define(long rva) throws Exception {
        Address address = currentProgram.getImageBase().add(rva);
        disassemble(address);
        Function fn = getFunctionAt(address);
        if (fn == null) {
            fn = createFunction(address, null);
        }
        if (fn == null) throw new IOException("Cannot define function at " + address);
        ghidra.app.cmd.function.CreateFunctionCmd.fixupFunctionBody(currentProgram, fn, monitor);
        List<JsonObject> aliases = methods.get(rva);
        if (aliases != null) fn.setName(symbol(aliases.get(0).get("Name").getAsString()), SourceType.USER_DEFINED);
        return fn;
    }

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 4) throw new IllegalArgumentException("script.json output-directory regex-file extra-rva-file");
        JsonObject root;
        try (Reader reader = Files.newBufferedReader(Path.of(args[0]), StandardCharsets.UTF_8)) {
            root = JsonParser.parseReader(reader).getAsJsonObject();
        }
        Pattern pattern = Pattern.compile(Files.readString(Path.of(args[2]), StandardCharsets.UTF_8).strip());
        Set<Long> selected = new LinkedHashSet<>();
        for (JsonElement element : root.getAsJsonArray("ScriptMethod")) {
            JsonObject method = element.getAsJsonObject();
            long rva = method.get("Address").getAsLong();
            methods.computeIfAbsent(rva, k -> new ArrayList<>()).add(method);
            if (pattern.matcher(method.get("Name").getAsString()).find()) selected.add(rva);
        }
        for (String line : Files.readAllLines(Path.of(args[3]), StandardCharsets.UTF_8)) {
            if (line.isBlank()) continue;
            long rva = Long.parseUnsignedLong(line.strip().replaceFirst("^0[xX]", ""), 16);
            if (!methods.containsKey(rva)) {
                JsonObject helper = new JsonObject();
                helper.addProperty("Name", String.format("NativeHelper_%08X", rva));
                helper.addProperty("Signature", "Unknown signature; inspect instructions and callers.");
                methods.put(rva, new ArrayList<>(List.of(helper)));
            }
            selected.add(rva);
        }
        if (selected.isEmpty()) throw new IllegalArgumentException("No methods matched");
        Path output = Path.of(args[1]);
        Files.createDirectories(output);
        for (long rva : selected) {
            monitor.checkCancelled();
            Function fn = define(rva);
            // Name direct callees so the generated C is readable. Preserve all aliases below.
            InstructionIterator instructions = currentProgram.getListing().getInstructions(fn.getBody(), true);
            Set<Long> callees = new LinkedHashSet<>();
            while (instructions.hasNext()) {
                Instruction instruction = instructions.next();
                if (!instruction.getFlowType().isCall()) continue;
                for (Address target : instruction.getFlows()) {
                    long calleeRva = target.subtract(currentProgram.getImageBase());
                    if (methods.containsKey(calleeRva)) callees.add(calleeRva);
                }
            }
            for (long callee : callees) define(callee);
        }
        DecompInterface decompiler = new DecompInterface();
        try {
            decompiler.openProgram(currentProgram);
            for (long rva : selected) {
                monitor.checkCancelled();
                Function fn = getFunctionAt(currentProgram.getImageBase().add(rva));
                String filename = String.format("%08X", rva) + "_" + fn.getName();
                DecompileResults result = decompiler.decompileFunction(fn, 60, monitor);
                if (!result.decompileCompleted()) throw new IOException("Decompilation failed: " + fn.getName() + " " + result.getErrorMessage());
                StringBuilder header = new StringBuilder("// Inferred C: types/signatures have not been fully recovered.\n");
                for (JsonObject alias : methods.get(rva)) {
                    header.append("// ").append(alias.get("Name").getAsString()).append("\n// ")
                        .append(alias.get("Signature").getAsString()).append("\n");
                }
                Files.writeString(output.resolve(filename + ".c"), header + result.getDecompiledFunction().getC());
                try (BufferedWriter writer = Files.newBufferedWriter(output.resolve(filename + ".asm"))) {
                    InstructionIterator instructions = currentProgram.getListing().getInstructions(fn.getBody(), true);
                    while (instructions.hasNext()) {
                        Instruction instruction = instructions.next();
                        writer.write(instruction.getAddress() + "  " + instruction);
                        for (Address target : instruction.getFlows()) {
                            List<JsonObject> aliases = methods.get(target.subtract(currentProgram.getImageBase()));
                            if (aliases != null) writer.write(" ; " + aliases.get(0).get("Name").getAsString());
                        }
                        writer.newLine();
                    }
                }
                println("EXPORTED " + filename);
            }
        } finally { decompiler.dispose(); }
        println("PRSTUTTER_EXPORT_OK count=" + selected.size());
    }
}
