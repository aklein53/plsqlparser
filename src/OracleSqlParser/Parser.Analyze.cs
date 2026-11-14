namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ANALYZE ----

    private Statement ParseAnalyze(Token start)
    {
        Next(); // ANALYZE
        var st = new AnalyzeStatement();
        if (AcceptKw("TABLE")) st.Target = AnalyzeTarget.Table;
        else if (AcceptKw("INDEX")) st.Target = AnalyzeTarget.Index;
        else { ExpectKw("CLUSTER"); st.Target = AnalyzeTarget.Cluster; }
        st.Name = ParseDottedName();
        if (IsKw("PARTITION") || IsKw("SUBPARTITION"))
        {
            var ps = Cur;
            var r = new PartitionReference { Subpartition = Next().Value == "SUBPARTITION" };
            ExpectOp("(");
            r.Name = ParseIdentifier();
            ExpectOp(")");
            st.Partition = Fin(r, ps);
        }

        if (IsKw("COMPUTE") && (IsKwAt(1, "STATISTICS") || IsKwAt(1, "SYSTEM")))
        {
            Next();
            st.SystemStatistics = AcceptKw("SYSTEM");
            ExpectKw("STATISTICS");
            st.Action = AnalyzeAction.ComputeStatistics;
        }
        else if (IsKw("ESTIMATE") && IsKwAt(1, "STATISTICS"))
        {
            Next(); Next();
            st.Action = AnalyzeAction.EstimateStatistics;
            if (AcceptKw("SAMPLE"))
            {
                st.Sample = ParseAdditive();
                if (AcceptKw("PERCENT")) st.SamplePercent = true;
                else ExpectKw("ROWS");
            }
        }
        else if (IsKw("DELETE") && IsKwAt(1, "STATISTICS"))
        {
            Next(); Next();
            st.Action = AnalyzeAction.DeleteStatistics;
        }
        else if (IsKw("VALIDATE") && IsKwAt(1, "STRUCTURE"))
        {
            Next(); Next();
            st.Action = AnalyzeAction.ValidateStructure;
            st.Cascade = AcceptKw("CASCADE");
            if (AcceptKw("ONLINE")) st.Online = true;
            else if (AcceptKw("OFFLINE")) st.Online = false;
        }
        else if (IsKw("LIST") && IsKwAt(1, "CHAINED") && IsKwAt(2, "ROWS"))
        {
            Next(); Next(); Next();
            st.Action = AnalyzeAction.ListChainedRows;
            if (AcceptKw("INTO")) st.IntoTable = ParseDottedName();
        }
        else st.Action = AnalyzeAction.Other;

        st.OtherClauses = ParseSyntaxItems(AtStatementEnd);
        return Fin(st, start);
    }
}
