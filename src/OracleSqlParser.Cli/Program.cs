using OracleSqlParser;

// Usage: oraclesqlparser [--shape] [--positions] [--compact] [-f file | -e "sql" | (stdin)]
bool positions = false, compact = false, shape = false;
string? sql = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--positions": positions = true; break;
        case "--compact": compact = true; break;
        case "--shape": shape = true; break;
        case "-f" when i + 1 < args.Length: sql = File.ReadAllText(args[++i]); break;
        case "-e" when i + 1 < args.Length: sql = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("oraclesqlparser [--shape] [--positions] [--compact] [-f file | -e \"sql\"]  (reads stdin if neither is given)");
            return 0;
        default: Console.Error.WriteLine($"Unknown argument: {args[i]}"); return 2;
    }
}
sql ??= Console.In.ReadToEnd();

var result = SqlParser.Parse(sql);
foreach (var err in result.Errors) Console.Error.WriteLine($"error {err}");
if (shape) foreach (var st in result.Script.Statements) Console.WriteLine(AstPrinter.Print(st));
else Console.WriteLine(AstJson.Serialize(result.Script, positions, !compact));
return result.Success ? 0 : 1;
