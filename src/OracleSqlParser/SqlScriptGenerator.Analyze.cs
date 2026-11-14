namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- ANALYZE ----------

        private void Analyze(AnalyzeStatement s)
        {
            Add("ANALYZE");
            Sp(); Add(s.Target switch { AnalyzeTarget.Table => "TABLE", AnalyzeTarget.Index => "INDEX", _ => "CLUSTER" });
            Sp(); Name(s.Name);
            if (s.Partition != null)
            {
                Sp(); Add(s.Partition.Subpartition ? "SUBPARTITION (" : "PARTITION (");
                Id(s.Partition.Name!);
                Add(")");
            }
            switch (s.Action)
            {
                case AnalyzeAction.ComputeStatistics:
                    Sp(); Add(s.SystemStatistics ? "COMPUTE SYSTEM STATISTICS" : "COMPUTE STATISTICS");
                    break;
                case AnalyzeAction.EstimateStatistics:
                    Sp(); Add("ESTIMATE STATISTICS");
                    if (s.Sample != null) { Sp(); Add("SAMPLE "); Expr(s.Sample); Add(s.SamplePercent ? " PERCENT" : " ROWS"); }
                    break;
                case AnalyzeAction.DeleteStatistics: Sp(); Add("DELETE STATISTICS"); break;
                case AnalyzeAction.ValidateStructure:
                    Sp(); Add("VALIDATE STRUCTURE");
                    if (s.Cascade) Add(" CASCADE");
                    if (s.Online != null) Add(s.Online == true ? " ONLINE" : " OFFLINE");
                    break;
                case AnalyzeAction.ListChainedRows:
                    Sp(); Add("LIST CHAINED ROWS");
                    if (s.IntoTable.Count > 0) { Add(" INTO "); Name(s.IntoTable); }
                    break;
            }
            if (s.OtherClauses.Count > 0) { Sp(); SyntaxList(s.OtherClauses); }
        }
    }
}
