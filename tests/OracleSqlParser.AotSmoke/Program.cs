// Native AOT smoke test: CI publishes this with PublishAot (trim/AOT warnings are errors) and runs it.
// It exercises the library paths most likely to regress under trimming: printer, JSON, generator, analysis, clone/compare.
using OracleSqlParser;
var sql = "WITH o AS (SELECT * FROM hr.orders WHERE r = :r) SELECT o.id, CASE WHEN o.x IS TRUE THEN 1 END FROM o JOIN c ON c.id = o.cid FETCH FIRST 5 ROWS ONLY";
var r = SqlParser.Parse(sql);
Console.WriteLine($"ok={r.Success} stmts={r.Script.Statements.Count}");
Console.WriteLine(AstPrinter.Print(r.Script.Statements[0])[..120]);
Console.WriteLine(AstJson.Serialize(r.Script.Statements[0])[..80]);
Console.WriteLine(new SqlScriptGenerator().Generate(r.Script));
Console.WriteLine(string.Join(", ", SqlAnalysis.GetTables(r.Script)));
var clone = r.Script.DeepClone();
Console.WriteLine($"clone equal={SqlNodeComparer.StructurallyEqual(clone, r.Script)}");
var bad = SqlParser.Parse("SELECT FROM", new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
if (!r.Success || !SqlNodeComparer.StructurallyEqual(clone, r.Script) || bad.Errors.Count == 0) { Console.Error.WriteLine("AOT smoke FAILED"); return 1; }
Console.WriteLine(bad.Errors[0].Code + " " + bad.Errors[0].FormatWithCaret("SELECT FROM").Replace("\n", " | "));
return 0;
