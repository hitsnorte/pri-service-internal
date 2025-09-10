using System;
using System.IO;

public static class Notas
{
    // Defina o diretório onde os arquivos de log serão armazenados
    private static readonly string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

    // Caminho do arquivo de log com base na data atual
    private static string logFilePath = GetLogFileName();

    // Método para definir o nome do arquivo de log com a data atual
    private static string GetLogFileName()
    {
        string currentDate = DateTime.Now.ToString("yyyy-MM-dd"); // Formato: yyyy-MM-dd
        return Path.Combine(logDirectory, $"Logs({currentDate}).txt");
    }

    // Método para registrar mensagens no log
    public static void Log(string message)
    {
        try
        {
            // Atualiza o caminho do arquivo se a data tiver mudado (novo dia)
            if (Path.GetFileName(logFilePath) != $"Logs({DateTime.Now:yyyy-MM-dd}).txt")
            {
                logFilePath = GetLogFileName();
            }

            // Cria o diretório se não existir
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Remove logs antigos
            RemoveOldLogs();

            // Escreve a mensagem no arquivo de log com a data e a hora
            using (var writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
            }
        }
        catch (Exception ex)
        {
            // Se houver um erro ao escrever no log, pode ser útil registrar isso de outra maneira
            Console.WriteLine("Erro ao registrar no log: " + ex.Message);
        }
    }

    // Método para remover arquivos de log mais antigos que 7 dias
    private static void RemoveOldLogs()
    {
        try
        {
            // Obtém todos os arquivos .txt no diretório de logs
            var logFiles = Directory.GetFiles(logDirectory, "Logs(*).txt");

            foreach (var file in logFiles)
            {
                // Extrai a data do nome do arquivo, que está no formato "Logs(yyyy-MM-dd).txt"
                string fileName = Path.GetFileNameWithoutExtension(file);
                string datePart = fileName.Replace("Logs(", "").Replace(")", "");

                if (DateTime.TryParse(datePart, out DateTime fileDate))
                {
                    // Se a data do arquivo for mais antiga que 7 dias, exclua o arquivo
                    if ((DateTime.Now - fileDate).TotalDays > 7)
                    {
                        File.Delete(file);
                        Console.WriteLine($"Arquivo de log excluído: {file}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro ao remover logs antigos: " + ex.Message);
        }
    }
}
