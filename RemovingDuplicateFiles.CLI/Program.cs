using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RemovingDuplicateFiles.CLI
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Программа для удаления дубликатов файлов.");

            Console.Write("Введите путь к основной папке (где все фото): ");
            string sourceDir = Console.ReadLine();

            Console.Write("Введите путь к папке для сравнения (альбомы): ");
            string targetDir = Console.ReadLine();

            string trashDir = @"C:\Users\daske\Desktop\trash";

            if (!Directory.Exists(sourceDir) || !Directory.Exists(targetDir))
            {
                Console.WriteLine("Одна из указанных папок не существует. Проверьте пути и попробуйте снова.");
                return;
            }

            if (!Directory.Exists(trashDir))
            {
                Directory.CreateDirectory(trashDir);
            }

            try
            {
                var targetHashes = GetFileHashes(targetDir);
                var sourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);

                int duplicatesFound = 0;

                Console.WriteLine($"\nПоиск дубликатов в '{sourceDir}'...");
                foreach (var sourceFile in sourceFiles)
                {
                    Console.WriteLine($"Обработка файла: {sourceFile}");
                    string sourceHash = GetFileHash(sourceFile);

                    if (targetHashes.Contains(sourceHash))
                    {
                        duplicatesFound++;
                        string fileName = Path.GetFileName(sourceFile);
                        string destPath = Path.Combine(trashDir, fileName);

                        // Если файл с таким именем уже есть в корзине, добавляем уникальный идентификатор
                        if (File.Exists(destPath))
                        {
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
                            string extension = Path.GetExtension(sourceFile);
                            destPath = Path.Combine(trashDir, $"{fileNameWithoutExt}_{Guid.NewGuid()}{extension}");
                        }

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Найден дубликат: {sourceFile}. Перемещение в {destPath}");
                        Console.ResetColor();
                        File.Move(sourceFile, destPath);
                    }
                }

                if (duplicatesFound == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\nДубликаты не найдены.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nОперация завершена. Перемещено {duplicatesFound} дубликатов в '{trashDir}'.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nПроизошла ошибка: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода.");
            Console.ReadKey();
        }

        static HashSet<string> GetFileHashes(string directory)
        {
            var hashes = new HashSet<string>();
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            Console.WriteLine($"\nВычисление хэшей для {files.Length} файлов в '{directory}'...");

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                Console.WriteLine($"({i + 1}/{files.Length}) Вычисление хэша для: {file}");
                string hash = GetFileHash(file);
                hashes.Add(hash);
            }

            return hashes;
        }

        static string GetFileHash(string filePath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
}
