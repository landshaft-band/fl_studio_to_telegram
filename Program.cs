using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Telegram.Bot;
using Telegram.Bot.Types.InputFiles;

class Program
{
    private static readonly string TelegramToken = "ТЕЛЕГРАМ ТОКЕН";
    private static readonly string ChannelId = "ID ТЕЛЕГРАМ КАНАЛА";
    private static readonly TelegramBotClient Bot = new TelegramBotClient(TelegramToken);
    private static readonly string FolderToWatch = @"ПУТЬ К ПАПКЕ ДЛЯ МОНИТОРИНГА";
    private static readonly int RetryDelaySeconds = 5;

    private static readonly string ConnectionString = "Server=localhost;Database=tg_fl_logs;User Id=admin;Password=admin;";
    private static ConcurrentDictionary<string, bool> SentFiles = new ConcurrentDictionary<string, bool>();

    static async Task Main(string[] args)
    {
        using (var watcher = new FileSystemWatcher(FolderToWatch))
        {
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
            watcher.Filter = "*.mp3";
            watcher.Created += OnCreated;
            watcher.EnableRaisingEvents = true;

            LogToDatabase(" Начато отслеживание папки: " + FolderToWatch);
            Console.WriteLine(" Начато отслеживание папки: " + FolderToWatch);
            await Task.Delay(-1);
        }
    }

    private static async void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (SentFiles.TryAdd(e.FullPath, false))
        {
            LogToDatabase($" Файл обнаружен: {e.FullPath}. Ожидание 1.5 минуты...");
            await Task.Delay(TimeSpan.FromMinutes(1.5));
            await TrySendFileAsync(e.FullPath);
        }
    }

    private static async Task TrySendFileAsync(string filePath)
    {
        while (true)
        {
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var inputFile = new InputOnlineFile(fileStream, Path.GetFileName(filePath));
                    await Bot.SendAudioAsync(ChannelId, inputFile);
                    LogToDatabase($" Успешно отправлено: {filePath}");
                    SentFiles[filePath] = true;
                    return;
                }
            }
            catch (IOException)
            {
                LogToDatabase($" Файл {filePath} занят другим процессом. Повтор через {RetryDelaySeconds} секунд.");
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
            }
            catch (Exception ex)
            {
                LogToDatabase($" Ошибка при отправке {filePath}: {ex.Message}");
                break;
            }
        }
    }

    private static void LogToDatabase(string message)
    {
        try
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new MySqlCommand("INSERT INTO logs (message) VALUES (@message)", connection))
                {
                    command.Parameters.AddWithValue("@message", message);
                    command.ExecuteNonQuery();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка логирования в БД: {ex.Message}");
        }
    }
}
