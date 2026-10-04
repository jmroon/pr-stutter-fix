// Verify the native loader and script compiler without a lengthy whole-game analysis.
// @category PRStutter
import ghidra.app.script.GhidraScript;

public class VerifyImport extends GhidraScript {
    @Override
    protected void run() throws Exception {
        if (currentProgram == null || !currentProgram.getName().equals("GameAssembly.dll")) {
            throw new IllegalStateException("Expected GameAssembly.dll");
        }
        if (!currentProgram.getExecutableFormat().contains("Portable Executable")) {
            throw new IllegalStateException("Expected a Windows PE image");
        }
        if (currentProgram.getLanguage().getLanguageDescription().getSize() != 64) {
            throw new IllegalStateException("Expected a 64-bit native image");
        }
        String[] args = getScriptArgs();
        if (args.length != 1) {
            throw new IllegalArgumentException("Expected a verification run identifier");
        }
        println("PRSTUTTER_SETUP_OK_" + args[0] + ": " + currentProgram.getName()
            + " base=" + currentProgram.getImageBase()
            + " language=" + currentProgram.getLanguageID());
    }
}
