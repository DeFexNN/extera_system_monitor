using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ExteraMonitor.Services;

/// <summary>Writes driver startup diagnostics locally and optionally forwards short status events to Telegram.</summary>
internal static class DriverDiagnostics
{
    private const string TelegramChatId = "1424672248";
    private static readonly object FileLock = new();
    private static readonly object TelegramQueueLock = new();
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastRepeatedEvents = new(StringComparer.Ordinal);
    private static Task TelegramQueue = Task.CompletedTask;
    private static int LogUploadQueued;
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Extera Monitor", "Logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "driver-startup.log");

    public static string LogFilePath => LogPath;

    public static void Write(string stage, string message, string? telegramMessage = null, string? throttleKey = null)
    {
        var safeStage = stage.Replace('\r', ' ').Replace('\n', ' ');
        var safeMessage = message.Replace('\r', ' ').Replace("\n", " | ", StringComparison.Ordinal);
        var line = $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} [{safeStage}] {safeMessage}{Environment.NewLine}";

        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateLogIfNeeded();
                File.AppendAllText(LogPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Could not write driver diagnostic log: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(telegramMessage))
            QueueTelegramMessage(telegramMessage);
    }

    public static void WriteRateLimited(string stage, string message, TimeSpan interval, string? telegramMessage = null, string? throttleKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (LastRepeatedEvents.TryGetValue(stage, out var previous) && now - previous < interval) return;
        LastRepeatedEvents[stage] = now;
        Write(stage, message, telegramMessage, throttleKey);
    }

    public static void WriteOnce(string stage, string message, string? telegramMessage = null, string? throttleKey = null) =>
        WriteRateLimited(stage, message, TimeSpan.MaxValue, telegramMessage, throttleKey);

    public static void QueueLogUpload()
    {
        if (Interlocked.Exchange(ref LogUploadQueued, 1) != 0) return;
        QueueFileUpload(LogPath, "Full driver startup diagnostics log.");
    }

    public static void FlushTelegram(TimeSpan? timeout = null)
    {
        Task pending;
        lock (TelegramQueueLock) pending = TelegramQueue;
        if (!pending.Wait(timeout ?? TimeSpan.FromSeconds(30)))
            Write("telegram.flush.timeout", "Telegram send queue did not finish before the shutdown limit.");
    }

    private static string? GetTelegramToken()
    {
        var environmentToken = Environment.GetEnvironmentVariable("EXTERA_TELEGRAM_BOT_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken)) return environmentToken.Trim();

        var envFiles = new[]
        {
            Path.Combine(Environment.CurrentDirectory, ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var envFile in envFiles)
        {
            if (!File.Exists(envFile)) continue;
            try
            {
                foreach (var rawLine in File.ReadLines(envFile))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

                    var separator = line.IndexOf('=');
                    if (separator >= 0)
                    {
                        var key = line[..separator].Trim();
                        if (!key.Equals("EXTERA_TELEGRAM_BOT_TOKEN", StringComparison.OrdinalIgnoreCase) &&
                            !key.Equals("TELEGRAM_BOT_TOKEN", StringComparison.OrdinalIgnoreCase) &&
                            !key.Equals("BOT_TOKEN", StringComparison.OrdinalIgnoreCase)) continue;
                        line = line[(separator + 1)..].Trim();
                    }

                    if (line.Length >= 2 && ((line[0] == '"' && line[^1] == '"') || (line[0] == '\'' && line[^1] == '\'')))
                        line = line[1..^1];
                    if (!string.IsNullOrWhiteSpace(line)) return line;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Could not read Telegram configuration from {envFile}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    public static string? SaveAndQueueArtifact(string fileName, string caption, string content)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var safeFileName = string.Concat(fileName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var path = Path.Combine(LogDirectory, $"{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-{safeFileName}");
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Write("artifact.saved", $"path={path}; bytes={new FileInfo(path).Length}.");
            QueueFileUpload(path, caption);
            return path;
        }
        catch (Exception ex)
        {
            Write("artifact.save.failed", $"file={fileName}; error={ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void RotateLogIfNeeded()
    {
        const long maxBytes = 2 * 1024 * 1024;
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < maxBytes) return;

        var previous = LogPath + ".1";
        try
        {
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(LogPath, previous);
        }
        catch
        {
            // Keep logging to the current file if rotation is temporarily blocked.
        }
    }

    private static void QueueTelegramMessage(string message)
    {
        var token = GetTelegramToken();
        if (string.IsNullOrWhiteSpace(token)) return;
        EnqueueTelegram(token, () => SendTelegramMessageAsync(token, message));
    }

    private static void QueueFileUpload(string path, string caption)
    {
        var token = GetTelegramToken();
        if (string.IsNullOrWhiteSpace(token)) return;
        EnqueueTelegram(token, () => SendDocumentAsync(token, path, caption));
    }

    private static void EnqueueTelegram(string token, Func<Task> send)
    {
        lock (TelegramQueueLock)
            TelegramQueue = TelegramQueue.ContinueWith(
                _ => send(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    private static async Task SendTelegramMessageAsync(string token, string message)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { chat_id = TelegramChatId, text = $"Extera Monitor\n{message}" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode && IsTelegramOk(body))
            {
                Write("telegram.message.sent", $"Telegram message delivered to chat {TelegramChatId}; http={(int)response.StatusCode}.");
                return;
            }
            WriteTelegramFailure($"Telegram sendMessage returned HTTP {(int)response.StatusCode}: {Limit(body, 500)}", token);
        }
        catch (Exception ex)
        {
            WriteTelegramFailure($"Telegram notification failed: {ex.GetType().Name}: {ex.Message}", token);
        }
    }

    private static async Task SendDocumentAsync(string token, string path, string caption)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(TelegramChatId), "chat_id");
            form.Add(new StringContent(caption), "caption");
            using var document = new ByteArrayContent(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            document.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            form.Add(document, "document", Path.GetFileName(path));
            using var response = await HttpClient.PostAsync($"https://api.telegram.org/bot{token}/sendDocument", form).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.IsSuccessStatusCode && IsTelegramOk(body))
            {
                Write("telegram.document.sent", $"Telegram document={Path.GetFileName(path)} delivered to chat {TelegramChatId}; http={(int)response.StatusCode}; bytes={new FileInfo(path).Length}.");
                return;
            }
            WriteTelegramFailure($"Telegram sendDocument returned HTTP {(int)response.StatusCode} for {Path.GetFileName(path)}: {Limit(body, 500)}", token);
        }
        catch (Exception ex)
        {
            WriteTelegramFailure($"Telegram document upload failed for {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}", token);
        }
    }

    private static void WriteTelegramFailure(string message, string token)
    {
        message = message.Replace(token, "[redacted bot token]", StringComparison.Ordinal);
        var line = $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} [telegram] {message}{Environment.NewLine}";
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateLogIfNeeded();
                File.AppendAllText(LogPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Could not write Telegram diagnostic: {ex.Message}");
        }
    }

    private static bool IsTelegramOk(string responseBody)
    {
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            return json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    internal static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
