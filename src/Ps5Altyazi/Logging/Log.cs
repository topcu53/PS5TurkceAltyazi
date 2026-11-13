using System;
using System.IO;
using System.Text;

namespace Ps5Altyazi.Logging;

public enum LogLevel { Bilgi, Uyari, Hata, Basari }

/// <summary>
/// Basit, iş parçacığı güvenli dosya + arayüz logu.
/// Dosyalar: %LOCALAPPDATA%\PS5TurkceAltyazi\logs\yakalama-YYYYMMDD.log
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5TurkceAltyazi");

    public static string LogDir => Path.Combine(AppDataDir, "logs");
    public static string CaptureDir => Path.Combine(AppDataDir, "captures");
    public static string CurrentFile => Path.Combine(LogDir, $"yakalama-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Her yazılan satır için tetiklenir (herhangi bir iş parçacığından).</summary>
    public static event Action<LogLevel, string>? Written;

    public static void Info(string msg) => Write(LogLevel.Bilgi, msg);
    public static void Warn(string msg) => Write(LogLevel.Uyari, msg);
    public static void Error(string msg) => Write(LogLevel.Hata, msg);
    public static void Success(string msg) => Write(LogLevel.Basari, msg);

    public static void Write(LogLevel level, string msg)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Label(level)}] {msg}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(CurrentFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Log yazılamazsa uygulama çökmemeli.
            }
        }
        try { Written?.Invoke(level, line); } catch { }
    }

    private static string Label(LogLevel l) => l switch
    {
        LogLevel.Bilgi => "BİLGİ",
        LogLevel.Uyari => "UYARI",
        LogLevel.Hata => "HATA",
        LogLevel.Basari => "BAŞARILI",
        _ => "?"
    };
}
