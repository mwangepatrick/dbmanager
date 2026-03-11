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
            public string Name { get; set; }
            public string Server { get; set; }
            public string Database { get; set; }
            public string UserId { get; set; }
            public string Password { get; set; }
            public int Port { get; set; }
        }


        private static readonly byte[] key = Encoding.UTF8.GetBytes("Ade87hjdw78sA9kfjT6fjEm2XcPQre01");
        private static readonly byte[] iv = Encoding.UTF8.GetBytes("Uj8mNp7dOw4rIyB2");

        private static string GetConfigPath()
        {
            var commonPath = "dbswap";
            if (!Directory.Exists(commonPath)) Directory.CreateDirectory(commonPath);
            return Path.Combine(commonPath, "dbConfig.json");
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

        public static List<DbConfig> LoadConfigs()
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return new List<DbConfig>();

            try
            {
                var encryptedJson = File.ReadAllText(path);
                var decryptedJson = DecryptString(encryptedJson);

                try
                {
                    var configs = JsonConvert.DeserializeObject<List<DbConfig>>(decryptedJson);
                    if (configs != null) return configs;
                }
                catch
                {
                    // Migration: try to load as single config
                    var singleConfig = JsonConvert.DeserializeObject<DbConfig>(decryptedJson);
                    if (singleConfig != null)
                    {
                        if (string.IsNullOrEmpty(singleConfig.Name)) singleConfig.Name = "Default";
                        return new List<DbConfig> { singleConfig };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configurations: {ex.Message}");
            }
            return new List<DbConfig>();
        }

        public static void SaveConfigs(List<DbConfig> configs)
        {
            var path = GetConfigPath();
            var json = JsonConvert.SerializeObject(configs, Formatting.Indented);
            var encryptedJson = EncryptString(json);
            File.WriteAllText(path, encryptedJson);
        }

        public static bool TestConnection(DbConfig config)
        {
            string connString = $"Server={config.Server};Database={config.Database};User Id={config.UserId};Password={config.Password};Port={config.Port};Timeout=10;";
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nConnection failed: {ex.Message}");
                return false;
            }
        }

        public static DbConfig PromptForConfig(DbConfig existing = null)
        {
            var config = new DbConfig();
            if (existing != null)
            {
                config.Name = existing.Name;
                config.Server = existing.Server;
                config.Database = existing.Database;
                config.UserId = existing.UserId;
                config.Password = existing.Password;
                config.Port = existing.Port;
            }
            else
            {
                config.Port = 5432;
                config.Database = "postgres";
            }

            Console.WriteLine(existing == null ? "\n--- Add New Connection ---" : "\n--- Edit Connection ---");

            Console.Write($"Enter connection name [{(existing?.Name ?? "New Connection")}]: ");
            var name = Console.ReadLine();
            if (!string.IsNullOrEmpty(name)) config.Name = name;
            else if (existing == null) config.Name = "New Connection";

            Console.Write($"Enter server [{(existing?.Server ?? "")}]: ");
            var server = Console.ReadLine();
            if (!string.IsNullOrEmpty(server)) config.Server = server;

            Console.Write($"Enter database [{(existing?.Database ?? "postgres")}]: ");
            var db = Console.ReadLine();
            if (!string.IsNullOrEmpty(db)) config.Database = db;

            Console.Write($"Enter user ID [{(existing?.UserId ?? "postgres")}]: ");
            var user = Console.ReadLine();
            if (!string.IsNullOrEmpty(user)) config.UserId = user;

            Console.Write($"Enter password [{(existing?.Password != null ? "****" : "")}]: ");
            var pass = Console.ReadLine();
            if (!string.IsNullOrEmpty(pass)) config.Password = pass;

            Console.Write($"Enter port [{(existing?.Port.ToString() ?? "5432")}]: ");
            var portInput = Console.ReadLine();
            if (int.TryParse(portInput, out int port)) config.Port = port;

            Console.WriteLine("Testing connection...");
            if (TestConnection(config))
            {
                Console.WriteLine("Connection test passed!");
                return config;
            }
            else
            {
                Console.Write("Connection test failed. Save anyway? (y/n): ");
                if (Console.ReadLine()?.ToLower() == "y") return config;
            }
            return null;
        }

        public static DbConfig SelectOrManageConfigs()
        {
            while (true)
            {
                var configs = LoadConfigs();
                Console.WriteLine("\n==============================");
                Console.WriteLine("   DATABASE CONNECTION MANAGER");
                Console.WriteLine("==============================");
                
                if (configs.Count == 0)
                {
                    Console.WriteLine("No connections saved.");
                }
                else
                {
                    for (int i = 0; i < configs.Count; i++)
                    {
                        Console.WriteLine($"{i + 1}. [{configs[i].Name}] {configs[i].Server}:{configs[i].Port} ({configs[i].Database})");
                    }
                }
                Console.WriteLine("------------------------------");
                Console.WriteLine("A. Add new connection");
                if (configs.Count > 0)
                {
                    Console.WriteLine("E. Edit a connection");
                    Console.WriteLine("D. Delete a connection");
                }
                Console.WriteLine("X. Exit");
                Console.Write("\nChoose a connection number or an option: ");

                string choice = Console.ReadLine()?.ToUpper();

                if (choice == "A")
                {
                    var newConfig = PromptForConfig();
                    if (newConfig != null)
                    {
                        configs.Add(newConfig);
                        SaveConfigs(configs);
                        Console.WriteLine("Connection added successfully.");
                    }
                }
                else if (choice == "E" && configs.Count > 0)
                {
                    Console.Write("Enter the number to edit: ");
                    if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= configs.Count)
                    {
                        var updated = PromptForConfig(configs[index - 1]);
                        if (updated != null)
                        {
                            configs[index - 1] = updated;
                            SaveConfigs(configs);
                            Console.WriteLine("Connection updated successfully.");
                        }
                    }
                }
                else if (choice == "D" && configs.Count > 0)
                {
                    Console.Write("Enter the number to delete: ");
                    if (int.TryParse(Console.ReadLine(), out int index) && index > 0 && index <= configs.Count)
                    {
                        Console.Write($"Are you sure you want to delete '{configs[index-1].Name}'? (y/n): ");
                        if (Console.ReadLine()?.ToLower() == "y")
                        {
                            configs.RemoveAt(index - 1);
                            SaveConfigs(configs);
                            Console.WriteLine("Connection deleted.");
                        }
                    }
                }
                else if (choice == "X")
                {
                    Environment.Exit(0);
                }
                else if (int.TryParse(choice, out int selIndex) && selIndex > 0 && selIndex <= configs.Count)
                {
                    var selected = configs[selIndex - 1];
                    Console.WriteLine($"\nConnecting to '{selected.Name}'...");
                    if (TestConnection(selected))
                    {
                        Console.WriteLine("Successfully connected!");
                        return selected;
                    }
                    else
                    {
                        Console.WriteLine("Could not connect. Please check settings or server status.");
                        Console.WriteLine("Press any key to return to menu...");
                        Console.ReadKey();
                    }
                }
                else
                {
                    Console.WriteLine("Invalid choice. Try again.");
                }
            }
        }

        static void Main(string[] args)
        {
            while (true)
            {
                DbConfig config = SelectOrManageConfigs();

                string connectionString =
                    $"Server={config.Server};Database={config.Database};User Id={config.UserId};Password={config.Password};Port={config.Port};Timeout=30;";
                NpgsqlConnection connection = new NpgsqlConnection(connectionString);

                bool backToConnections = false;
                while (!backToConnections)
                {
                    Console.WriteLine($"\n--- [{config.Name}] Active Connection ---");
                    Console.WriteLine("1. Delete database");
                    Console.WriteLine("2. Access database (psql)");
                    Console.WriteLine("3. Rename database");
                    Console.WriteLine("4. Backup database");
                    Console.WriteLine("5. Import new database");
                    Console.WriteLine("6. Switch / Manage connections");
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
                            backToConnections = true;
                            break;
                        case "7":
                            return;
                        default:
                            Console.WriteLine("Invalid choice. Please try again.");
                            break;
                    }
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
