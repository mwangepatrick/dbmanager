using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Npgsql;

namespace DbManager
{
    class Program
    {
        public class DbConfig
        {
            public string Server { get; set; }
            public string Database { get; set; }
            public string UserId { get; set; }
            public string Password { get; set; }
            public int Port { get; set; }
        }


        private static readonly byte[] key = Encoding.UTF8.GetBytes("Ade87hjdw78sA9kfjT6fjEm2XcPQre01");
        private static readonly byte[] iv = Encoding.UTF8.GetBytes("Uj8mNp7dOw4rIyB2");

        public static DbConfig GetOrCreateDbConfig(bool rst = false)
        {
            var commonPath = "dbswap";
            if (!Directory.Exists(commonPath)) Directory.CreateDirectory(commonPath);
            var jsonConfigPath = Path.Combine(commonPath, "dbConfig.json");
            DbConfig config;

            if (!rst && File.Exists(jsonConfigPath))
            {
                var encryptedJson = File.ReadAllText(jsonConfigPath);
                var decryptedJson = DecryptString(encryptedJson);
                config = JsonConvert.DeserializeObject<DbConfig>(decryptedJson);
            }
            else
            {
                config = new DbConfig();
                Console.Write("Enter the server name: ");
                config.Server = Console.ReadLine();
                Console.Write("Enter the database name: ");
                config.Database = Console.ReadLine();
                Console.Write("Enter the user ID: ");
                config.UserId = Console.ReadLine();
                Console.Write("Enter the password: ");
                config.Password = Console.ReadLine();

                // Prompt for port
                Console.Write("Enter the port (leave empty for default 5432): ");
                var portInput = Console.ReadLine();
                if (int.TryParse(portInput, out int port))
                {
                    config.Port = port;
                }
                else
                {
                    config.Port = 5432;
                }

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                var encryptedJson = EncryptString(json);
                File.WriteAllText(jsonConfigPath, encryptedJson);
            }

            return config;
        }


        private static string EncryptString(string plainText)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        using (var sw = new StreamWriter(cs))
                        {
                            sw.Write(plainText);
                        }
                    }

                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        private static string DecryptString(string cipherText)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                using (var ms = new MemoryStream(Convert.FromBase64String(cipherText)))
                {
                    using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    {
                        using (var sr = new StreamReader(cs))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
            }
        }

        static void Main(string[] args)
        {

            DbConfig config = GetOrCreateDbConfig();

            string connectionString =
                $"Server={config.Server};Database={config.Database};User Id={config.UserId};Password={config.Password};Port={config.Port};Timeout=30;";
            NpgsqlConnection connection = new NpgsqlConnection(connectionString);

            while (true)
            {
                Console.WriteLine("\nChoose a task:");
                Console.WriteLine("1. Delete database");
                Console.WriteLine("2. Access database");
                Console.WriteLine("3. Rename database");
                Console.WriteLine("4. Backup database");
                Console.WriteLine("5. Import new database");
                Console.WriteLine("6. Change database settings");
                Console.WriteLine("7. Exit");
                Console.Write("Enter your choice: ");

                string choice = Console.ReadLine();
                switch (choice)
                {
                    case "1":
                        DeleteDatabase(connection, config);
                        break;
                    case "2":
                        AccessDatabase(config);
                        break;
                    case "3":
                        Console.Write("Enter the current database name: ");
                        string currentDatabaseName = Console.ReadLine();

                        Console.Write("Enter the new name for the database: ");
                        string newDatabaseName = Console.ReadLine();

                        RenameDatabase(connection, config, currentDatabaseName, newDatabaseName);

                        break;
                    case "4":
                        BackupDatabase(config);
                        break;
                    case "5":
                        ImportNewDatabase(connection, config);
                        break;
                    case "6":
                        config = GetOrCreateDbConfig(true);

                        connectionString =
                            $"Server={config.Server};Database={config.Database};User Id={config.UserId};Password={config.Password};Port={config.Port}";
                        connection.ConnectionString = connectionString;
                        break;
                    case "7":
                        return;
                    default:
                        Console.WriteLine("Invalid choice. Please try again.");
                        break;
                }
            }
        }

        public static void AccessDatabase(DbConfig config)
        {
            Console.Write("Enter the name of the database to access: ");
            string dbName = Console.ReadLine();

            config.Database = dbName;

            try
            {
                Environment.SetEnvironmentVariable("PGPASSWORD", config.Password);

                string psqlCommand = $"psql -h {config.Server} -U {config.UserId} -p {config.Port} -d {config.Database}";

                ProcessStartInfo procStartInfo = new ProcessStartInfo("cmd", "/c start cmd /k " + psqlCommand)
                {
                    UseShellExecute = true
                };

                using (Process proc = new Process { StartInfo = procStartInfo })
                {
                    proc.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while accessing the database: {ex.Message}");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PGPASSWORD", null);
            }
        }

        public static void DeleteDatabase(NpgsqlConnection connection, DbConfig config)
        {
            try
            {
                string listDatabasesConnectionString =
                    $"Server={config.Server};Database=postgres;User Id={config.UserId};Password={config.Password};";
                using (var listDatabasesConnection = new NpgsqlConnection(listDatabasesConnectionString))
                {
                    listDatabasesConnection.Open();
                    string listDatabasesSql = "SELECT datname FROM pg_database WHERE datistemplate = false;";
                    using (var command = new NpgsqlCommand(listDatabasesSql, listDatabasesConnection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            List<string> databases = new List<string>();
                            while (reader.Read())
                            {
                                databases.Add(reader.GetString(0));
                            }

                            Console.WriteLine("Available databases:");
                            for (int i = 0; i < databases.Count; i++)
                            {
                                Console.WriteLine($"{i + 1}: {databases[i]}");
                            }

                            Console.Write("Enter the number of the database you want to delete: ");
                            if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > databases.Count)
                            {
                                Console.WriteLine("Invalid choice. Operation canceled.");
                                return;
                            }

                            config.Database = databases[choice - 1];
                            Console.WriteLine($"You have selected to delete the database '{config.Database}'.");
                        }
                    }
                }

                Console.Write(
                    $"Are you sure you want to delete the database '{config.Database}'? This action cannot be undone (yes/no): ");
                string confirmation = Console.ReadLine()?.ToLower();

                if (confirmation != "yes")
                {
                    Console.WriteLine("Database deletion canceled.");
                    return;
                }

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }

                string defaultConnectionString =
                    $"Server={config.Server};Database=postgres;User Id={config.UserId};Password={config.Password};";
                using (var defaultConnection = new NpgsqlConnection(defaultConnectionString))
                {
                    defaultConnection.Open();

                    string sql = $"DROP DATABASE IF EXISTS \"{config.Database}\";";
                    using (var command = new NpgsqlCommand(sql, defaultConnection))
                    {
                        command.ExecuteNonQuery();
                        Console.WriteLine($"Database '{config.Database}' deleted successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while deleting the database: {ex.Message}");
            }
        }

        static void RenameDatabase(NpgsqlConnection connection, DbConfig config, string currentDatabaseName,
            string newDatabaseName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newDatabaseName))
                {
                    Console.WriteLine("Invalid database name. Operation canceled.");
                    return;
                }

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }

                string defaultConnectionString =
                    $"Server={config.Server};Port={config.Port};Database=postgres;User Id={config.UserId};Password={config.Password};Timeout=30;";

                using (var defaultConnection = new NpgsqlConnection(defaultConnectionString))
                {
                    defaultConnection.Open();

                    // Disconnect all other users from the database
                    string terminateSql = $@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{currentDatabaseName}'
                AND pid <> pg_backend_pid();";

                    using (var terminateCommand = new NpgsqlCommand(terminateSql, defaultConnection))
                    {
                        terminateCommand.ExecuteNonQuery();
                        Console.WriteLine($"Disconnected all users from the database '{currentDatabaseName}'.");
                    }

                    // Rename the database
                    string renameSql = $"ALTER DATABASE \"{currentDatabaseName}\" RENAME TO \"{newDatabaseName}\";";
                    using (var renameCommand = new NpgsqlCommand(renameSql, defaultConnection))
                    {
                        renameCommand.ExecuteNonQuery();
                        Console.WriteLine($"Database '{currentDatabaseName}' has been renamed to '{newDatabaseName}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while renaming the database: {ex.Message}");
            }
        }

        static void ImportNewDatabase(NpgsqlConnection connection, DbConfig config)
        {
            try
            {
                Console.Write("Enter the backup file name (including path): ");
                string backupFileName = Console.ReadLine();

                Console.Write("Enter the name for the new database to import: ");
                string newDatabaseName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(backupFileName))
                {
                    Console.WriteLine("Error: Backup file name is empty or null. Please provide a valid backup file name. Operation canceled.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(newDatabaseName))
                {
                    Console.WriteLine("Error: New database name is empty or null. Please provide a valid database name. Operation canceled.");
                    return;
                }

                if (!File.Exists(backupFileName.Replace("\"","")))
                {
                    Console.WriteLine($"Error: The file '{backupFileName}' does not exist. Please check the file path and try again. Operation canceled.");
                    return;
                }


                if (DatabaseExists(connection, newDatabaseName))
                {
                    string timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                    var renamedNewDatabaseName = $"{newDatabaseName}_{timestamp}";
                    Console.WriteLine($"Database already exists. New database name set to '{renamedNewDatabaseName}'.");
                    RenameDatabase(connection, config, newDatabaseName, renamedNewDatabaseName);
                }

                // Close the existing connection if open
                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }

                string defaultConnectionString =
                    $"Server={config.Server};Port={config.Port};Database=postgres;User Id={config.UserId};Password={config.Password};Timeout=30;";

                using (var defaultConnection = new NpgsqlConnection(defaultConnectionString))
                {
                    defaultConnection.Open();
                    Console.WriteLine($"Connected to the default database '{config.Database}'.");

                    // Create the new database
                    string createDbSql = $"CREATE DATABASE \"{newDatabaseName}\";";
                    using (var createDbCommand = new NpgsqlCommand(createDbSql, defaultConnection))
                    {
                        Console.WriteLine($"Creating database '{newDatabaseName}'...");
                        createDbCommand.ExecuteNonQuery();
                        Console.WriteLine($"Database '{newDatabaseName}' created successfully.");
                    }
                }

                // Now import the backup
                string importCommand =
                    $"pg_restore --host={config.Server} --port={config.Port} --username={config.UserId} --dbname={newDatabaseName} --verbose \"{backupFileName}\"";
                Console.WriteLine($"Importing backup from '{backupFileName}' into database '{newDatabaseName}'...");

                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pg_restore",
                    Arguments =
                        $"--host={config.Server} --port={config.Port} --username={config.UserId} --dbname={newDatabaseName} --verbose \"{backupFileName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                processInfo.EnvironmentVariables["PGPASSWORD"] = config.Password;

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    // Read output and error streams asynchronously
                    var outputTask = Task.Run(() =>
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            string line = process.StandardOutput.ReadLine();
                            Console.WriteLine(line);
                        }
                    });

                    var errorTask = Task.Run(() =>
                    {
                        while (!process.StandardError.EndOfStream)
                        {
                            string line = process.StandardError.ReadLine();
                            Console.WriteLine(line);
                        }
                    });

                    Task.WhenAll(outputTask, errorTask);

                    process.WaitForExit();
                    Console.WriteLine("Import completed with exit code: " + process.ExitCode);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during the import: {ex.Message}");
            }
        }

        static bool DatabaseExists(NpgsqlConnection connection, string databaseName)
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            string checkDbSql = $"SELECT 1 FROM pg_database WHERE datname = '{databaseName}'";
            using (var command = new NpgsqlCommand(checkDbSql, connection))
            {
                var result = command.ExecuteScalar();
                connection.Close();
                return result != null;
            }

        }

        public static void BackupDatabase(DbConfig config)
        {
            try
            {
                Console.Write("Enter the name of the database to backup: ");
                string dbName = Console.ReadLine();
                Console.WriteLine($"Database name: {dbName}");

                string timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                string backupFileName = $"{dbName}_{timestamp}.backup";
                Console.WriteLine($"Backup file name: {backupFileName}");

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string backupsFolderPath = Path.Combine(desktopPath, "backups");
                Console.WriteLine($"Backups folder path: {backupsFolderPath}");

                if (!Directory.Exists(backupsFolderPath))
                {
                    Directory.CreateDirectory(backupsFolderPath);
                    Console.WriteLine("Created 'backups' folder on desktop.");
                }
                else
                {
                    Console.WriteLine("'backups' folder already exists on desktop.");
                }

                string backupFilePath = Path.Combine(backupsFolderPath, backupFileName);
                Console.WriteLine($"Backup file path: {backupFilePath}");

                Environment.SetEnvironmentVariable("PGPASSWORD", config.Password);
                Console.WriteLine("Environment variable PGPASSWORD set.");

                string dumpCommand = $"pg_dump -h {config.Server} -U {config.UserId} -p {config.Port} -F c -b -v -f \"{backupFilePath}\" {dbName}";
                Console.WriteLine($"Dump command: {dumpCommand}");

                ProcessStartInfo procStartInfo = new ProcessStartInfo("cmd", "/c " + dumpCommand)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Console.WriteLine("Starting the backup process...");
                using (var proc = new Process {StartInfo = procStartInfo})
                {
                    proc.OutputDataReceived += (sender, data) =>
                    {
                        if (!string.IsNullOrEmpty(data.Data)) Console.WriteLine($"[Output] {data.Data}");
                    };
                    proc.ErrorDataReceived += (sender, data) =>
                    {
                        if (!string.IsNullOrEmpty(data.Data)) Console.WriteLine($"[Error] {data.Data}");
                    };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                    Console.WriteLine($"Process exited with code: {proc.ExitCode}");
                }
                Environment.SetEnvironmentVariable("PGPASSWORD", null);
                Console.WriteLine("Environment variable PGPASSWORD cleared.");

                Console.WriteLine($"Backup successful! File saved as: {backupFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during backup: {ex.Message}\n{ex.StackTrace}");
            }
        }


    }
}
