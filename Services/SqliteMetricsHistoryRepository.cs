using ExteraMonitor.Models;
using Microsoft.Data.Sqlite;

namespace ExteraMonitor.Services;

public interface IMetricsHistoryRepository
{
    string DatabasePath { get; }
    void Store(SystemSnapshot snapshot);
}

public interface ICpuCustomizationRepository
{
    CpuCustomizationSettings LoadCpuCustomization();
    void SaveCpuCustomization(CpuCustomizationSettings settings);
}

public interface IThemePaletteRepository
{
    ThemePaletteSettings LoadThemePalette();
    void SaveThemePalette(ThemePaletteSettings settings);
}

public sealed class SqliteMetricsHistoryRepository : IMetricsHistoryRepository, ICpuCustomizationRepository, IThemePaletteRepository
{
    private readonly string _connectionString;
    public string DatabasePath { get; }

    public SqliteMetricsHistoryRepository(bool readOnly = false)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExteraMonitor");
        if (!readOnly) Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, "metrics-history.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate }.ToString();
        if (!readOnly) Initialize();
    }

    public void Store(SystemSnapshot snapshot)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO snapshots(timestamp_utc,cpu_usage,ExteraMonitorDrivererature,memory_usage,memory_total,storage_usage,download_mbps,upload_mbps,process_count,uptime_seconds) VALUES($timestamp,$cpu,$temperature,$memory,$memoryTotal,$storage,$download,$upload,$processes,$uptime); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O")); command.Parameters.AddWithValue("$cpu", snapshot.CpuUsage); command.Parameters.AddWithValue("$temperature", snapshot.CpuTemperature); command.Parameters.AddWithValue("$memory", snapshot.MemoryUsage); command.Parameters.AddWithValue("$memoryTotal", snapshot.MemoryTotal); command.Parameters.AddWithValue("$storage", snapshot.StorageUsage); command.Parameters.AddWithValue("$download", snapshot.DownloadMbps); command.Parameters.AddWithValue("$upload", snapshot.UploadMbps); command.Parameters.AddWithValue("$processes", snapshot.ProcessCount); command.Parameters.AddWithValue("$uptime", snapshot.Uptime.TotalSeconds);
        var snapshotId = (long)(command.ExecuteScalar() ?? 0L);
        InsertCores(connection, transaction, snapshotId, snapshot.Cores); InsertDisks(connection, transaction, snapshotId, snapshot.Disks); InsertSensors(connection, transaction, snapshotId, snapshot.Sensors); transaction.Commit();
    }

    public CpuCustomizationSettings LoadCpuCustomization()
    {
        var widgets = new List<CpuWidgetLayout>();
        var accents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using (var widgetCommand = connection.CreateCommand())
        {
            widgetCommand.CommandText = "SELECT kind,x,y,width,height,style,accent_color FROM cpu_widgets ORDER BY id";
            using var reader = widgetCommand.ExecuteReader();
            while (reader.Read()) widgets.Add(new CpuWidgetLayout(reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetInt32(5), reader.GetString(6)));
        }
        using (var accentCommand = connection.CreateCommand())
        {
            accentCommand.CommandText = "SELECT name,color FROM cpu_accents";
            using var reader = accentCommand.ExecuteReader();
            while (reader.Read()) accents[reader.GetString(0)] = reader.GetString(1);
        }
        var styles = new List<CpuWidgetStyleSettings>();
        using (var styleCommand = connection.CreateCommand())
        {
            styleCommand.CommandText = "SELECT kind,style,background_color,text_color,muted_color,border_color,accent_color FROM cpu_widget_styles ORDER BY kind,style";
            using var reader = styleCommand.ExecuteReader();
            while (reader.Read()) styles.Add(new CpuWidgetStyleSettings(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        }
        var historySeries = "Load,Temp,Clock,Power";
        var historyRange = "10m";
        var sensorsExpanded = false;
        using (var dashboardCommand = connection.CreateCommand())
        {
            dashboardCommand.CommandText = "SELECT history_series,history_range,sensors_expanded FROM cpu_dashboard_settings WHERE id=1";
            using var reader = dashboardCommand.ExecuteReader();
            if (reader.Read())
            {
                historySeries = reader.GetString(0);
                historyRange = reader.GetString(1);
                sensorsExpanded = reader.GetInt32(2) != 0;
            }
        }
        return new CpuCustomizationSettings(widgets, accents, styles, historySeries, historyRange, sensorsExpanded);
    }

    public void SaveCpuCustomization(CpuCustomizationSettings settings)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var transaction = connection.BeginTransaction();
        using (var clearWidgets = connection.CreateCommand()) { clearWidgets.Transaction = transaction; clearWidgets.CommandText = "DELETE FROM cpu_widgets"; clearWidgets.ExecuteNonQuery(); }
        foreach (var widget in settings.Widgets)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO cpu_widgets(kind,x,y,width,height,style,accent_color) VALUES($kind,$x,$y,$width,$height,$style,$accent)";
            command.Parameters.AddWithValue("$kind", widget.Kind); command.Parameters.AddWithValue("$x", widget.X); command.Parameters.AddWithValue("$y", widget.Y); command.Parameters.AddWithValue("$width", widget.Width); command.Parameters.AddWithValue("$height", widget.Height); command.Parameters.AddWithValue("$style", widget.Style); command.Parameters.AddWithValue("$accent", widget.AccentColorHex); command.ExecuteNonQuery();
        }
        using (var clearAccents = connection.CreateCommand()) { clearAccents.Transaction = transaction; clearAccents.CommandText = "DELETE FROM cpu_accents"; clearAccents.ExecuteNonQuery(); }
        foreach (var accent in settings.AccentColors)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO cpu_accents(name,color) VALUES($name,$color)";
            command.Parameters.AddWithValue("$name", accent.Key); command.Parameters.AddWithValue("$color", accent.Value); command.ExecuteNonQuery();
        }
        using (var clearStyles = connection.CreateCommand()) { clearStyles.Transaction = transaction; clearStyles.CommandText = "DELETE FROM cpu_widget_styles"; clearStyles.ExecuteNonQuery(); }
        foreach (var style in settings.WidgetStyles)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO cpu_widget_styles(kind,style,background_color,text_color,muted_color,border_color,accent_color) VALUES($kind,$style,$background,$text,$muted,$border,$accent)";
            command.Parameters.AddWithValue("$kind", style.Kind); command.Parameters.AddWithValue("$style", style.Style); command.Parameters.AddWithValue("$background", style.BackgroundHex); command.Parameters.AddWithValue("$text", style.TextHex); command.Parameters.AddWithValue("$muted", style.MutedHex); command.Parameters.AddWithValue("$border", style.BorderHex); command.Parameters.AddWithValue("$accent", style.AccentHex); command.ExecuteNonQuery();
        }
        using (var dashboard = connection.CreateCommand())
        {
            dashboard.Transaction = transaction;
            dashboard.CommandText = "INSERT INTO cpu_dashboard_settings(id,history_series,history_range,sensors_expanded) VALUES(1,$series,$range,$expanded) ON CONFLICT(id) DO UPDATE SET history_series=excluded.history_series,history_range=excluded.history_range,sensors_expanded=excluded.sensors_expanded";
            dashboard.Parameters.AddWithValue("$series", settings.HistorySeries);
            dashboard.Parameters.AddWithValue("$range", settings.HistoryRange);
            dashboard.Parameters.AddWithValue("$expanded", settings.SensorsExpanded ? 1 : 0);
            dashboard.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public ThemePaletteSettings LoadThemePalette()
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT name,color FROM theme_palette";
        using var reader = command.ExecuteReader();
        while (reader.Read()) colors[reader.GetString(0)] = reader.GetString(1);
        return new ThemePaletteSettings(colors);
    }

    public void SaveThemePalette(ThemePaletteSettings settings)
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var transaction = connection.BeginTransaction();
        using (var clear = connection.CreateCommand()) { clear.Transaction = transaction; clear.CommandText = "DELETE FROM theme_palette"; clear.ExecuteNonQuery(); }
        foreach (var color in settings.Colors)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO theme_palette(name,color) VALUES($name,$color)";
            command.Parameters.AddWithValue("$name", color.Key); command.Parameters.AddWithValue("$color", color.Value); command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS snapshots(id INTEGER PRIMARY KEY, timestamp_utc TEXT NOT NULL, cpu_usage REAL, ExteraMonitorDrivererature REAL, memory_usage REAL, memory_total REAL, storage_usage REAL, download_mbps REAL, upload_mbps REAL, process_count INTEGER, uptime_seconds REAL); CREATE TABLE IF NOT EXISTS core_samples(snapshot_id INTEGER NOT NULL, core_index INTEGER NOT NULL, usage_percent REAL NOT NULL); CREATE TABLE IF NOT EXISTS disk_samples(snapshot_id INTEGER NOT NULL, name TEXT NOT NULL, label TEXT, used_gb REAL, total_gb REAL, usage_percent REAL); CREATE TABLE IF NOT EXISTS sensor_samples(snapshot_id INTEGER NOT NULL, hardware_name TEXT NOT NULL, name TEXT NOT NULL, type TEXT NOT NULL, value REAL NOT NULL, unit TEXT); CREATE TABLE IF NOT EXISTS cpu_widgets(id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL UNIQUE, x REAL NOT NULL, y REAL NOT NULL, width REAL NOT NULL, height REAL NOT NULL, style INTEGER NOT NULL, accent_color TEXT NOT NULL DEFAULT '#395B64'); CREATE TABLE IF NOT EXISTS cpu_accents(name TEXT PRIMARY KEY, color TEXT NOT NULL); CREATE TABLE IF NOT EXISTS cpu_widget_styles(id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, style INTEGER NOT NULL, background_color TEXT NOT NULL, text_color TEXT NOT NULL, muted_color TEXT NOT NULL, border_color TEXT NOT NULL, accent_color TEXT NOT NULL, UNIQUE(kind,style)); CREATE TABLE IF NOT EXISTS cpu_dashboard_settings(id INTEGER PRIMARY KEY CHECK(id=1),history_series TEXT NOT NULL DEFAULT 'Load,Temp,Clock,Power',history_range TEXT NOT NULL DEFAULT '10m',sensors_expanded INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS theme_palette(name TEXT PRIMARY KEY, color TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_snapshots_timestamp ON snapshots(timestamp_utc);";
        command.ExecuteNonQuery();
        try { using var migration = connection.CreateCommand(); migration.CommandText = "ALTER TABLE cpu_widgets ADD COLUMN accent_color TEXT NOT NULL DEFAULT '#395B64'"; migration.ExecuteNonQuery(); } catch (SqliteException) { }
    }

    private static void InsertCores(SqliteConnection connection, SqliteTransaction transaction, long snapshotId, IEnumerable<CoreMetric> cores) { foreach (var core in cores) Insert(connection, transaction, "INSERT INTO core_samples VALUES($id,$index,$value)", ("$id", snapshotId), ("$index", core.Index), ("$value", core.UsagePercent)); }
    private static void InsertDisks(SqliteConnection connection, SqliteTransaction transaction, long snapshotId, IEnumerable<DiskMetric> disks) { foreach (var disk in disks) Insert(connection, transaction, "INSERT INTO disk_samples VALUES($id,$name,$label,$used,$total,$usage)", ("$id", snapshotId), ("$name", disk.Name), ("$label", disk.VolumeLabel), ("$used", disk.UsedGigabytes), ("$total", disk.TotalGigabytes), ("$usage", disk.UsagePercent)); }
    private static void InsertSensors(SqliteConnection connection, SqliteTransaction transaction, long snapshotId, IEnumerable<HardwareSensorMetric> sensors) { foreach (var sensor in sensors) Insert(connection, transaction, "INSERT INTO sensor_samples VALUES($id,$hardware,$name,$type,$value,$unit)", ("$id", snapshotId), ("$hardware", sensor.HardwareName), ("$name", sensor.Name), ("$type", sensor.Type), ("$value", sensor.Value), ("$unit", sensor.Unit)); }
    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] values) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value); command.ExecuteNonQuery(); }
}
