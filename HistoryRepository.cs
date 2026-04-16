using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PerformanceCalculator2;

/// <summary>SQLite 持久化：按导入批次存储绩效明细。</summary>
public sealed class HistoryRepository
{
    private readonly string _connectionString;

    public HistoryRepository()
    {
        // 用户主目录下持久化（%USERPROFILE%\PerformanceCalculator2\）
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PerformanceCalculator2");
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "history.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        EnsureDatabase();
    }

    private SqliteConnection Open() => new SqliteConnection(_connectionString);

    private static void OpenAndConfigure(SqliteConnection cn)
    {
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
    }

    private void EnsureDatabase()
    {
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS import_batches (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_utc TEXT NOT NULL,
  month_key TEXT NOT NULL,
  source_files TEXT
);
CREATE TABLE IF NOT EXISTS perf_records (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  batch_id INTEGER NOT NULL,
  month_key TEXT NOT NULL,
  rp_no TEXT,
  test_time REAL NOT NULL,
  actual_time REAL NOT NULL,
  operator_name TEXT NOT NULL,
  test_date_utc TEXT,
  FOREIGN KEY (batch_id) REFERENCES import_batches(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_perf_month ON perf_records(month_key);
CREATE INDEX IF NOT EXISTS idx_perf_batch ON perf_records(batch_id);
";
        cmd.ExecuteNonQuery();
    }

    public static string ResolvePrimaryMonthKey(IReadOnlyList<TestRecord> rows)
    {
        if (rows == null || rows.Count == 0) return "";
        var withMonth = rows.Where(r => !string.IsNullOrEmpty(r.MonthKey)).ToList();
        if (withMonth.Count == 0) return "";
        return withMonth
            .GroupBy(r => r.MonthKey, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key, StringComparer.Ordinal)
            .First().Key;
    }

    public long InsertImport(IReadOnlyList<TestRecord> rows, string sourceFiles, IProgress<(int current, int total)>? rowProgress = null)
    {
        if (rows == null || rows.Count == 0) throw new ArgumentException("无数据可保存", nameof(rows));
        string monthKey = ResolvePrimaryMonthKey(rows);
        if (string.IsNullOrEmpty(monthKey))
            monthKey = "未知";

        using var cn = Open();
        OpenAndConfigure(cn);
        using var tx = cn.BeginTransaction();
        long batchId;
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO import_batches (created_utc, month_key, source_files) VALUES ($c,$m,$s);";
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$m", monthKey);
            cmd.Parameters.AddWithValue("$s", sourceFiles ?? "");
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT last_insert_rowid();";
            batchId = Convert.ToInt64(cmd.ExecuteScalar()!, CultureInfo.InvariantCulture);
        }

        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            const int chunkSize = 120;
            int n = rows.Count;
            for (int start = 0; start < n; start += chunkSize)
            {
                int take = Math.Min(chunkSize, n - start);
                var sb = new StringBuilder(320 + take * 96);
                sb.Append("INSERT INTO perf_records (batch_id, month_key, rp_no, test_time, actual_time, operator_name, test_date_utc) VALUES ");
                for (int k = 0; k < take; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append("($b,$mk").Append(k).Append(",$rp").Append(k).Append(",$tt").Append(k)
                        .Append(",$at").Append(k).Append(",$op").Append(k).Append(",$dt").Append(k).Append(')');
                }
                sb.Append(';');
                cmd.CommandText = sb.ToString();
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$b", batchId);
                for (int k = 0; k < take; k++)
                {
                    var r = rows[start + k];
                    cmd.Parameters.AddWithValue("$mk" + k, r.MonthKey ?? "");
                    cmd.Parameters.AddWithValue("$rp" + k, r.RpNo ?? "");
                    cmd.Parameters.AddWithValue("$tt" + k, r.TestTimeMinutes);
                    cmd.Parameters.AddWithValue("$at" + k, r.ActualTimeMinutes);
                    cmd.Parameters.AddWithValue("$op" + k, r.OperatorName ?? "");
                    cmd.Parameters.AddWithValue("$dt" + k,
                        r.TestDate.HasValue ? r.TestDate.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : (object)DBNull.Value);
                }

                cmd.ExecuteNonQuery();
                int done = start + take;
                rowProgress?.Report((done, n));
            }
        }

        tx.Commit();
        return batchId;
    }

    public IReadOnlyList<ImportBatchInfo> ListBatches()
    {
        var list = new List<ImportBatchInfo>();
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT b.id, b.created_utc, b.month_key, b.source_files,
       (SELECT COUNT(*) FROM perf_records r WHERE r.batch_id = b.id) AS cnt
FROM import_batches b
ORDER BY b.id DESC;";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            list.Add(new ImportBatchInfo
            {
                Id = rd.GetInt64(0),
                CreatedUtc = DateTime.Parse(rd.GetString(1), null, DateTimeStyles.RoundtripKind),
                MonthKey = rd.IsDBNull(2) ? "" : rd.GetString(2),
                SourceFiles = rd.IsDBNull(3) ? "" : rd.GetString(3),
                RowCount = rd.GetInt32(4)
            });
        }
        return list;
    }

    public void DeleteBatch(long batchId)
    {
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM import_batches WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", batchId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> ListDistinctMonths()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT month_key FROM perf_records WHERE length(trim(month_key)) > 0 ORDER BY month_key;";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            set.Add(rd.GetString(0));
        return set.ToList();
    }

    public string? GetLatestImportMonthKey()
    {
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT month_key FROM import_batches ORDER BY id DESC LIMIT 1;";
        var o = cmd.ExecuteScalar();
        return o == null || o is DBNull ? null : Convert.ToString(o);
    }

    public List<TestRecord> LoadRecordsForMonths(ISet<string> months)
    {
        var result = new List<TestRecord>();
        if (months == null || months.Count == 0) return result;

        using var cn = Open();
        OpenAndConfigure(cn);
        var sb = new StringBuilder("SELECT month_key, rp_no, test_time, actual_time, operator_name, test_date_utc FROM perf_records WHERE month_key IN (");
        int i = 0;
        using var cmd = cn.CreateCommand();
        foreach (var m in months)
        {
            if (i > 0) sb.Append(',');
            string pname = "$m" + i;
            sb.Append(pname);
            cmd.Parameters.AddWithValue(pname, m);
            i++;
        }
        sb.Append(");");
        cmd.CommandText = sb.ToString();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var tr = new TestRecord
            {
                MonthKey = rd.IsDBNull(0) ? "" : rd.GetString(0),
                RpNo = rd.IsDBNull(1) ? "" : rd.GetString(1),
                TestTimeMinutes = rd.GetDouble(2),
                ActualTimeMinutes = rd.GetDouble(3),
                OperatorName = rd.IsDBNull(4) ? "" : rd.GetString(4)
            };
            if (!rd.IsDBNull(5))
            {
                if (DateTime.TryParse(rd.GetString(5), null, DateTimeStyles.RoundtripKind, out var dt))
                    tr.TestDate = dt.ToLocalTime();
            }
            result.Add(tr);
        }
        return result;
    }

    public bool HasAnyData()
    {
        using var cn = Open();
        OpenAndConfigure(cn);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM perf_records LIMIT 1);";
        return Convert.ToInt64(cmd.ExecuteScalar()!) == 1;
    }
}
