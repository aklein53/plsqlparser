using System.Diagnostics;
using System.Reflection;
using AstGen;

// Regenerates src/OracleSqlParser/Generated/*.g.cs.
//   dotnet run --project tools/AstGen                      build the library without Generated/ in a temp copy, then generate into the repo
//   dotnet run --project tools/AstGen -- --assembly <dll>  reflect over an already built assembly (skips the temp build)
//   dotnet run --project tools/AstGen -- --out <dir>       write somewhere else
string? assemblyPath = null, outDir = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--assembly" && i + 1 < args.Length) assemblyPath = args[++i];
    else if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
    else { Console.Error.WriteLine("usage: AstGen [--assembly <OracleSqlParser.dll>] [--out <dir>]"); return 2; }
}

var root = FindRepoRoot();
outDir ??= Path.Combine(root, "src", "OracleSqlParser", "Generated");
string? temp = null;
try
{
    if (assemblyPath is null)
    {
        // Bootstrap: build the library from a copy of its sources WITHOUT Generated/, so a stale or empty folder cannot break the build.
        temp = Path.Combine(Path.GetTempPath(), "astgen-" + Guid.NewGuid().ToString("N"));
        var srcCopy = Path.Combine(temp, "src", "OracleSqlParser");
        CopyDir(Path.Combine(root, "src", "OracleSqlParser"), srcCopy);
        File.Copy(Path.Combine(root, "README.md"), Path.Combine(temp, "README.md"));
        var psi = new ProcessStartInfo("dotnet", $"build \"{Path.Combine(srcCopy, "OracleSqlParser.csproj")}\" -c Release -f net10.0 -o \"{Path.Combine(temp, "out")}\" -nologo -v q")
        { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) { Console.Error.WriteLine(output); Console.Error.WriteLine("bootstrap build failed"); return 1; }
        assemblyPath = Path.Combine(temp, "out", "OracleSqlParser.dll");
    }

    var files = AstGenerator.Generate(Assembly.LoadFrom(Path.GetFullPath(assemblyPath)));
    Directory.CreateDirectory(outDir);
    foreach (var stale in Directory.GetFiles(outDir, "*.g.cs").Where(f => !files.ContainsKey(Path.GetFileName(f)))) File.Delete(stale);
    foreach (var (name, text) in files)
    {
        File.WriteAllText(Path.Combine(outDir, name), text);
        Console.WriteLine($"wrote {Path.Combine(outDir, name)} ({text.Count(c => c == '\n')} lines)");
    }
    return 0;
}
finally
{
    if (temp is not null) try { Directory.Delete(temp, true); } catch { }
}

static string FindRepoRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "OracleSqlParser.slnx"))) return d.FullName;
    throw new InvalidOperationException("Run from inside the repository (OracleSqlParser.slnx not found).");
}

static void CopyDir(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    foreach (var d in Directory.GetDirectories(from))
    {
        var name = Path.GetFileName(d);
        if (name is "bin" or "obj" or "Generated") continue;
        CopyDir(d, Path.Combine(to, name));
    }
}
