# OracleSqlParser.Cli

Command-line tool for [OracleSqlParser](https://github.com/aklein53/plsqlparser), a hand-written parser for Oracle SQL
and PL/SQL. It parses the input and prints the abstract syntax tree, either as JSON or as the compact tree used by the
library's shape tests.

## Install

```
dotnet tool install -g OracleSqlParser.Cli --prerelease
oraclesqlparser -e "SELECT 1 FROM dual"
```

The tool is built for net8.0 and net10.0. Use `--tool-path <dir>` instead of `-g` to install it into a directory of
your choice.

## Usage

```
oraclesqlparser [--shape] [--positions] [--compact] [-f file | -e "sql"]
```

| Flag | Meaning |
|------|---------|
| `-e "sql"` | Parse the SQL text given on the command line. |
| `-f file` | Read the SQL from a file. |
| (neither) | Read the SQL from standard input. |
| `--shape` | Print each top-level statement as a compact tree (one line per statement) instead of JSON. |
| `--positions` | Include `Start`/`End`/`Line`/`Column` for each node in the JSON output. Has no effect with `--shape`. |
| `--compact` | Print single-line JSON instead of indented JSON. Has no effect with `--shape`. |
| `-h`, `--help` | Show usage. |

Syntax errors are written to standard error, one per line. The tree is still printed for the statements that parsed.

Exit codes: `0` when the input parsed without errors, `1` when there were parse errors, `2` for an unknown argument.

Examples:

```
oraclesqlparser -f migration.sql --positions > migration.json
echo "UPDATE emp SET sal = sal * 1.1 WHERE deptno = 10" | oraclesqlparser --shape
```

See the [main README](https://github.com/aklein53/plsqlparser/blob/main/README.md) for the library API and the
[coverage matrix](https://github.com/aklein53/plsqlparser/blob/main/COVERAGE.md).
