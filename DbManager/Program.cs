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

        private class SireSetup
        {
            public string SourceDatabase { get; set; }
            public string NewDatabaseName { get; set; }
            public string BranchName { get; set; }
            public string BranchNumber { get; set; }
            public string MpesaTillNumber { get; set; }
            public int NumberOfTills { get; set; }
            public List<string> TablesToClean { get; set; } = new List<string>();
        }

        private class TableInfo
        {
            public string Schema { get; set; }
            public string Name { get; set; }
            public long RowCount { get; set; }
        }


        private static readonly byte[] key = Encoding.UTF8.GetBytes("Ade87hjdw78sA9kfjT6fjEm2XcPQre01");
        private static readonly byte[] iv = Encoding.UTF8.GetBytes("Uj8mNp7dOw4rIyB2");

        private static string GetConfigPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var commonPath = Path.Combine(appDataPath, "DbManager");
            if (!Directory.Exists(commonPath)) Directory.CreateDirectory(commonPath);

            var newPath = Path.Combine(commonPath, "dbConfig.json");

            // Migration logic: move from old local 'dbswap' folder if it exists
            var oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dbswap", "dbConfig.json");
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try
                {
                    File.Copy(oldPath, newPath);
                    Console.WriteLine("Migrated configuration to AppData.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to migrate config: {ex.Message}");
                }
            }

            return newPath;
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
                    Console.WriteLine("1. Show databases");
                    Console.WriteLine("2. Delete database");
                    Console.WriteLine("3. Access database (psql)");
                    Console.WriteLine("4. Rename database");
                    Console.WriteLine("5. Backup database");
                    Console.WriteLine("6. Import new database");
                    Console.WriteLine("7. [Pawa] Sire - clone sample database");
                    Console.WriteLine("8. [Pawa] Transaction reset");
                    Console.WriteLine("9. [Pawa] VACUUM/FREEZE/ANALYZE");
                    Console.WriteLine("10. [Pawa] Show table row counts");
                    Console.WriteLine("11. Go back to main menu");
                    if (!IsLocalServer(config.Server))
                    {
                        Console.WriteLine("12. [Pawa] Pull remote database to local (dump + restore under same name)");
                    }
                    Console.WriteLine("X. Exit");
                    Console.Write("Enter your choice: ");

                    string choice = Console.ReadLine()?.Trim().ToUpperInvariant();
                    switch (choice)
                    {
                        case "1":
                            ShowDatabases(config);
                            break;
                        case "2":
                            DeleteDatabase(connection, config);
                            break;
                        case "3":
                            AccessDatabase(config);
                            break;
                        case "4":
                            Console.Write($"Enter the current database name [{config.Database}]: ");
                            string currentDatabaseName = Console.ReadLine();
                            if (string.IsNullOrEmpty(currentDatabaseName)) currentDatabaseName = config.Database;

                            Console.Write("Enter the new name for the database: ");
                            string newDatabaseName = Console.ReadLine();

                            RenameDatabase(connection, config, currentDatabaseName, newDatabaseName);

                            break;
                        case "5":
                            BackupDatabase(config);
                            break;
                        case "6":
                            ImportNewDatabase(connection, config);
                            break;
                        case "7":
                            PawaSire(config);
                            break;
                        case "8":
                            TransactionReset(config);
                            break;
                        case "9":
                            VacuumAnalyze(config);
                            break;
                        case "10":
                            ShowTableRowCounts(config);
                            break;
                        case "11":
                            backToConnections = true;
                            break;
                        case "12":
                            if (IsLocalServer(config.Server))
                                Console.WriteLine("Invalid choice. Please try again.");
                            else
                                PullRemoteToLocal(config);
                            break;
                        case "X":
                            return;
                        default:
                            Console.WriteLine("Invalid choice. Please try again.");
                            break;
                    }
                }
            }
        }

        private static string QuoteIdentifier(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string BuildConnectionString(DbConfig config, string database)
        {
            return $"Server={config.Server};Port={config.Port};Database={database};User Id={config.UserId};Password={config.Password};Timeout=30;";
        }

        private static SireSetup PromptSireSetup(DbConfig config)
        {
            var setup = new SireSetup();
            var databases = GetDatabases(config);
            if (databases.Count == 0)
            {
                Console.WriteLine("No databases were found. Pawa Sire canceled.");
                return null;
            }

            Console.WriteLine("\n--- [Pawa] Sire Setup ---");
            for (int i = 0; i < databases.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {databases[i]}");
            }

            Console.Write($"Choose the source database [{config.Database}]: ");
            string sourceInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(sourceInput))
            {
                setup.SourceDatabase = config.Database;
            }
            else if (int.TryParse(sourceInput, out int sourceIndex) && sourceIndex > 0 && sourceIndex <= databases.Count)
            {
                setup.SourceDatabase = databases[sourceIndex - 1];
            }
            else if (databases.Contains(sourceInput))
            {
                setup.SourceDatabase = sourceInput;
            }
            else
            {
                Console.WriteLine("Invalid source database. Pawa Sire canceled.");
                return null;
            }

            Console.Write("New database name: ");
            setup.NewDatabaseName = Console.ReadLine()?.Trim();

            while (true)
            {
                Console.WriteLine("\nReview clone setup:");
                Console.WriteLine($"Source database: {setup.SourceDatabase}");
                Console.WriteLine($"New database: {setup.NewDatabaseName}");
                Console.Write("Enter E to edit, or Y to continue: ");
                string review = Console.ReadLine()?.Trim().ToUpperInvariant();
                if (review == "Y") return setup;
                if (review != "E") continue;

                Console.Write($"Source database [{setup.SourceDatabase}]: ");
                string value = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(value)) setup.SourceDatabase = value.Trim();
                Console.Write($"New database name [{setup.NewDatabaseName}]: ");
                value = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(value)) setup.NewDatabaseName = value.Trim();
            }
        }

        private static int ReadPositiveInteger(string prompt, int defaultValue = 0)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input) && defaultValue > 0) return defaultValue;
                if (int.TryParse(input, out int value) && value > 0) return value;
                Console.WriteLine("Enter a whole number greater than zero.");
            }
        }

        private static void PawaSire(DbConfig config)
        {
            SireSetup setup = PromptSireSetup(config);
            if (setup == null || string.IsNullOrWhiteSpace(setup.NewDatabaseName))
            {
                Console.WriteLine("New database name is required. Pawa Sire canceled.");
                return;
            }

            try
            {
                using (var adminConnection = new NpgsqlConnection(BuildConnectionString(config, "postgres")))
                {
                    adminConnection.Open();
                    bool sourceConnectionsDisabled = false;
                    bool sourceAllowedConnections = true;
                    NpgsqlConnection disconnectConnection = adminConnection;
                    bool temporaryDisconnectConnection = false;

                    using (var existsCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name);", adminConnection))
                    {
                        existsCommand.Parameters.AddWithValue("name", setup.NewDatabaseName);
                        if ((bool)existsCommand.ExecuteScalar())
                        {
                            Console.WriteLine($"Database '{setup.NewDatabaseName}' already exists. Pawa Sire canceled.");
                            return;
                        }
                    }

                    try
                    {
                        using (var allowConnectionsCommand = new NpgsqlCommand("SELECT datallowconn FROM pg_database WHERE datname = @sourceDatabase;", adminConnection))
                        {
                            allowConnectionsCommand.Parameters.AddWithValue("sourceDatabase", setup.SourceDatabase);
                            object result = allowConnectionsCommand.ExecuteScalar();
                            if (result == null)
                            {
                                Console.WriteLine($"Source database '{setup.SourceDatabase}' was not found. Pawa Sire canceled.");
                                return;
                            }
                            sourceAllowedConnections = (bool)result;
                        }

                        Console.WriteLine($"Blocking new sessions to source database '{setup.SourceDatabase}'...");
                        using (var blockCommand = new NpgsqlCommand($"ALTER DATABASE {QuoteIdentifier(setup.SourceDatabase)} ALLOW_CONNECTIONS false;", adminConnection))
                        {
                            blockCommand.ExecuteNonQuery();
                            sourceConnectionsDisabled = true;
                        }

                        Console.WriteLine($"Disconnecting active sessions from source database '{setup.SourceDatabase}'...");
                        const string terminateSql = @"
                            SELECT pg_terminate_backend(pid)
                            FROM pg_stat_activity
                            WHERE datname = @sourceDatabase
                              AND pid <> pg_backend_pid();";
                        try
                        {
                            using (var terminateCommand = new NpgsqlCommand(terminateSql, disconnectConnection))
                            {
                                terminateCommand.Parameters.AddWithValue("sourceDatabase", setup.SourceDatabase);
                                terminateCommand.ExecuteNonQuery();
                            }
                        }
                        catch (PostgresException ex) when (ex.SqlState == "42501")
                        {
                            Console.WriteLine("The configured user cannot disconnect one or more privileged sessions.");
                            DbConfig disconnectConfig = PromptDisconnectConfig(config);
                            if (disconnectConfig == null) throw;

                            disconnectConnection = new NpgsqlConnection(BuildConnectionString(disconnectConfig, "postgres"));
                            disconnectConnection.Open();
                            temporaryDisconnectConnection = true;

                            using (var terminateCommand = new NpgsqlCommand(terminateSql, disconnectConnection))
                            {
                                terminateCommand.Parameters.AddWithValue("sourceDatabase", setup.SourceDatabase);
                                terminateCommand.ExecuteNonQuery();
                            }
                        }

                        using (var sessionsCommand = new NpgsqlCommand(@"
                            SELECT COUNT(*)
                            FROM pg_stat_activity
                            WHERE datname = @sourceDatabase
                              AND pid <> pg_backend_pid();", adminConnection))
                        {
                            sessionsCommand.Parameters.AddWithValue("sourceDatabase", setup.SourceDatabase);
                            long activeSessions = (long)sessionsCommand.ExecuteScalar();
                            if (activeSessions > 0)
                            {
                                throw new InvalidOperationException($"Could not disconnect {activeSessions} session(s) from source database '{setup.SourceDatabase}'.");
                            }
                        }

                        string cloneSql = $"CREATE DATABASE {QuoteIdentifier(setup.NewDatabaseName)} WITH TEMPLATE {QuoteIdentifier(setup.SourceDatabase)};";
                        Console.WriteLine($"Cloning '{setup.SourceDatabase}' to '{setup.NewDatabaseName}'...");
                        using (var cloneCommand = new NpgsqlCommand(cloneSql, adminConnection))
                        {
                            cloneCommand.ExecuteNonQuery();
                        }
                    }
                    finally
                    {
                        if (sourceConnectionsDisabled)
                        {
                            using (var restoreCommand = new NpgsqlCommand($"ALTER DATABASE {QuoteIdentifier(setup.SourceDatabase)} ALLOW_CONNECTIONS {sourceAllowedConnections.ToString().ToLowerInvariant()};", disconnectConnection))
                            {
                                restoreCommand.ExecuteNonQuery();
                            }
                        }

                        if (temporaryDisconnectConnection)
                        {
                            disconnectConnection.Dispose();
                        }
                    }
                }

                Console.WriteLine($"Pawa Sire clone completed. Sample database: {setup.NewDatabaseName}");
                Console.WriteLine("Transaction reset was not run. Use option 8 when you are ready.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pawa Sire failed: {ex.Message}");
            }
        }

        private static DbConfig PromptDisconnectConfig(DbConfig originalConfig)
        {
            Console.WriteLine("\n--- Temporary Disconnect Credentials ---");
            Console.WriteLine("These credentials are used only to disconnect source-database sessions.");
            Console.Write($"Server [{originalConfig.Server}]: ");
            string server = Console.ReadLine();
            Console.Write($"Port [{originalConfig.Port}]: ");
            string portInput = Console.ReadLine();
            Console.Write("Privileged user: ");
            string user = Console.ReadLine();
            Console.Write("Privileged password: ");
            string password = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("Privileged user and password are required. Disconnect canceled.");
                return null;
            }

            var disconnectConfig = new DbConfig
            {
                Server = string.IsNullOrWhiteSpace(server) ? originalConfig.Server : server.Trim(),
                Port = int.TryParse(portInput, out int port) ? port : originalConfig.Port,
                UserId = user.Trim(),
                Password = password
            };
            return disconnectConfig;
        }

        private static void TransactionReset(DbConfig config)
        {
            string databaseName = PromptDatabaseName(config, "database to reset");
            if (string.IsNullOrWhiteSpace(databaseName)) return;

            try
            {
                using (var databaseConnection = new NpgsqlConnection(BuildConnectionString(config, databaseName)))
                {
                    databaseConnection.Open();
                    bool schemasDeleted = DeleteConfirmedBranchSchemas(databaseConnection);
                    ResetTypeTableLastNumbers(databaseConnection);
                    var tables = GetTransactionalTables(databaseConnection);
                    DisplayPopulatedTables(GetPopulatedTables(databaseConnection));
                    ReviewCleanupTables(tables);

                    if (tables.Count == 0)
                    {
                        if (schemasDeleted) VacuumDatabase(databaseConnection);
                        Console.WriteLine("No tables selected. Transaction reset canceled.");
                        return;
                    }

                    Console.WriteLine("The following tables will be emptied:");
                    foreach (var table in tables) Console.WriteLine($"- {table.Key} ({table.Value:N0} rows)");
                    Console.Write("Type RESET to continue: ");
                    if (!string.Equals(Console.ReadLine()?.Trim(), "RESET", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Transaction reset canceled.");
                        return;
                    }

                    CleanTransactionalTables(databaseConnection, tables);
                    Console.WriteLine($"Transaction reset completed for '{databaseName}'.");
                    VacuumDatabase(databaseConnection);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Transaction reset failed: {ex.Message}");
            }
        }

        private static void VacuumAnalyze(DbConfig config)
        {
            string databaseName = PromptDatabaseName(config, "database to vacuum");
            if (string.IsNullOrWhiteSpace(databaseName)) return;

            try
            {
                using (var databaseConnection = new NpgsqlConnection(BuildConnectionString(config, databaseName)))
                {
                    databaseConnection.Open();
                    VacuumDatabase(databaseConnection);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VACUUM failed: {ex.Message}");
            }
        }

        private static void ShowTableRowCounts(DbConfig config)
        {
            string databaseName = PromptDatabaseName(config, "database to inspect");
            if (string.IsNullOrWhiteSpace(databaseName)) return;

            try
            {
                using (var databaseConnection = new NpgsqlConnection(BuildConnectionString(config, databaseName)))
                {
                    databaseConnection.Open();
                    DisplayAllTableRowCounts(GetAllTableRowCounts(databaseConnection));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read table row counts: {ex.Message}");
            }
        }

        private static string PromptDatabaseName(DbConfig config, string purpose)
        {
            var databases = GetDatabases(config);
            if (databases.Count == 0)
            {
                Console.WriteLine("No databases were found.");
                return null;
            }

            Console.WriteLine($"\nSelect the {purpose}:");
            for (int i = 0; i < databases.Count; i++) Console.WriteLine($"{i + 1}. {databases[i]}");
            Console.Write("Enter a number or database name: ");
            string input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int index) && index > 0 && index <= databases.Count) return databases[index - 1];
            if (databases.Contains(input)) return input;
            Console.WriteLine("Invalid database. Operation canceled.");
            return null;
        }

        private static bool DeleteConfirmedBranchSchemas(NpgsqlConnection connection)
        {
            var schemas = new List<string>();
            bool deleted = false;
            const string schemaSql = @"
                SELECT schema_name
                FROM information_schema.schemata
                WHERE schema_name LIKE 'br\_%' ESCAPE '\'
                ORDER BY schema_name;";
            using (var command = new NpgsqlCommand(schemaSql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read()) schemas.Add(reader.GetString(0));
            }

            foreach (string schema in schemas)
            {
                Console.Write($"Delete schema '{schema}' and all its tables? (y/n): ");
                if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) continue;

                using (var dropCommand = new NpgsqlCommand($"DROP SCHEMA {QuoteIdentifier(schema)} CASCADE;", connection))
                {
                    dropCommand.ExecuteNonQuery();
                    deleted = true;
                    Console.WriteLine($"Deleted schema '{schema}'.");
                }
            }

            return deleted;
        }

        private static List<TableInfo> GetPopulatedTables(NpgsqlConnection connection)
        {
            var tables = new List<TableInfo>();
            var candidates = new List<TableInfo>();
            const string sql = @"
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND table_schema NOT LIKE 'pg\_%' ESCAPE '\'
                ORDER BY table_schema, table_name;";
            using (var command = new NpgsqlCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    candidates.Add(new TableInfo
                    {
                        Schema = reader.GetString(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            foreach (TableInfo table in candidates)
            {
                using (var countCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM {QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)};", connection))
                {
                    table.RowCount = (long)countCommand.ExecuteScalar();
                    if (table.RowCount > 0) tables.Add(table);
                }
            }
            return tables;
        }

        private static List<TableInfo> GetAllTableRowCounts(NpgsqlConnection connection)
        {
            var tables = new List<TableInfo>();
            const string sql = @"
                SELECT table_schema, table_name
                FROM information_schema.tables
                WHERE table_type = 'BASE TABLE'
                  AND table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND table_schema NOT LIKE 'pg\_%' ESCAPE '\'
                ORDER BY table_schema, table_name;";
            using (var command = new NpgsqlCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(new TableInfo
                    {
                        Schema = reader.GetString(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            foreach (TableInfo table in tables)
            {
                using (var countCommand = new NpgsqlCommand(
                    $"SELECT COUNT(*) FROM {QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)};",
                    connection))
                {
                    table.RowCount = (long)countCommand.ExecuteScalar();
                }
            }

            return tables;
        }

        private static void DisplayAllTableRowCounts(List<TableInfo> tables)
        {
            Console.WriteLine("\nAll application tables by row count:");
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            Console.WriteLine("| Schema                       | Table                          | Row count  |");
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            foreach (TableInfo table in tables.OrderByDescending(table => table.RowCount).ThenBy(table => table.Schema).ThenBy(table => table.Name))
            {
                Console.WriteLine($"| {TrimForTable(table.Schema, 28),-28} | {TrimForTable(table.Name, 30),-30} | {table.RowCount,10:N0} |");
            }
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            if (tables.Count == 0) Console.WriteLine("No application tables were found.");
        }

        private static void ResetTypeTableLastNumbers(NpgsqlConnection connection)
        {
            var typeTables = new List<TableInfo>();
            const string sql = @"
                SELECT DISTINCT c.table_schema, c.table_name
                FROM information_schema.columns c
                JOIN information_schema.tables t
                  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                WHERE t.table_type = 'BASE TABLE'
                  AND c.column_name = 'last_number'
                  AND c.table_name LIKE '%\_type' ESCAPE '\'
                  AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
                  AND c.table_schema NOT LIKE 'pg\_%' ESCAPE '\'
                ORDER BY c.table_schema, c.table_name;";
            using (var command = new NpgsqlCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    typeTables.Add(new TableInfo
                    {
                        Schema = reader.GetString(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            foreach (TableInfo table in typeTables)
            {
                Console.Write($"Reset last_number in {table.Schema}.{table.Name}? (y/n): ");
                if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Skipped {table.Schema}.{table.Name}.");
                    continue;
                }

                using (var updateCommand = new NpgsqlCommand(
                    $"UPDATE {QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)} SET {QuoteIdentifier("last_number")} = 0;",
                    connection))
                {
                    int updatedRows = updateCommand.ExecuteNonQuery();
                    Console.WriteLine($"Reset last_number in {table.Schema}.{table.Name} ({updatedRows:N0} row(s)).");
                }
            }
        }

        private static void DisplayPopulatedTables(List<TableInfo> tables)
        {
            Console.WriteLine("\nRemaining tables with data:");
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            Console.WriteLine("| Schema                       | Table                          | Row count  |");
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            foreach (TableInfo table in tables.OrderByDescending(table => table.RowCount).ThenBy(table => table.Schema).ThenBy(table => table.Name))
            {
                Console.WriteLine($"| {TrimForTable(table.Schema, 28),-28} | {TrimForTable(table.Name, 30),-30} | {table.RowCount,10:N0} |");
            }
            Console.WriteLine("+------------------------------+--------------------------------+------------+");
            if (tables.Count == 0) Console.WriteLine("No remaining tables contain data.");
        }

        private static string TrimForTable(string value, int maxLength)
        {
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength - 3) + "...";
        }

        private static Dictionary<string, long> GetTransactionalTables(NpgsqlConnection connection)
        {
            var tables = new Dictionary<string, long>();
            foreach (TableInfo table in GetPopulatedTables(connection))
            {
                string normalized = table.Name.ToLowerInvariant();
                bool explicitResetTable = IsExplicitResetTable(table.Schema, normalized);
                 if ((table.Schema.Equals("cb", StringComparison.OrdinalIgnoreCase) &&
                     normalized.Equals("ledger", StringComparison.Ordinal)) ||
                    (table.Schema.Equals("public", StringComparison.OrdinalIgnoreCase) &&
                     normalized.Equals("m_payments", StringComparison.Ordinal))) continue;

                if (table.Name.Equals("branch", StringComparison.OrdinalIgnoreCase) ||
                    (!explicitResetTable && (normalized.Contains("master") || normalized.Contains("config") ||
                    normalized.Contains("setting") || normalized.Contains("reference") ||
                    normalized.Contains("lookup") || normalized.Contains("till")))) continue;
                if (normalized.EndsWith("_type", StringComparison.Ordinal)) continue;

                if (explicitResetTable || normalized == "pl_trans_details" || normalized == "stock_pic" ||
                    normalized == "stock_loc" || normalized == "upd_log" ||
                    normalized.EndsWith("_log", StringComparison.Ordinal) ||
                    normalized.Contains("_trans") || normalized.Contains("st_") ||
                    normalized.Contains("transaction") || normalized.Contains("sale") ||
                    normalized.Contains("payment") || normalized.Contains("receipt") ||
                    normalized.Contains("invoice") || normalized.Contains("order") ||
                    normalized.Contains("ledger") || normalized.Contains("journal") ||
                    normalized.Contains("audit") || normalized.Contains("session") ||
                    normalized.Contains("mpesa"))
                {
                    tables[$"{table.Schema}.{table.Name}"] = table.RowCount;
                }
            }

            return tables;
        }

        private static bool IsExplicitResetTable(string schema, string normalizedTableName)
        {
            if (schema.Equals("promo", StringComparison.OrdinalIgnoreCase) && normalizedTableName == "promo_master") return true;

            return normalizedTableName == "cheque_iss" || normalizedTableName == "cheque_rec" ||
                normalizedTableName == "banking_summary" || normalizedTableName == "user_permission_details" ||
                normalizedTableName == "till_trans" || normalizedTableName == "till_trans_details" ||
                normalizedTableName == "till_payment_details" || normalizedTableName == "zed" ||
                normalizedTableName == "eod" || normalizedTableName == "price_hist" ||
                normalizedTableName == "hq_user" || normalizedTableName == "hq_operator" ||
                normalizedTableName == "m_eod" || normalizedTableName == "credit_notes" ||
                normalizedTableName == "deposits";
        }

        private static void ReviewCleanupTables(Dictionary<string, long> tables)
        {
            if (tables.Count == 0)
            {
                Console.WriteLine("No transaction-like tables were detected. No tables will be cleaned.");
                return;
            }

            var selected = new Dictionary<string, long>();
            Console.WriteLine("\nSelect transaction tables one by one:");
            foreach (var table in tables.ToList())
            {
                Console.Write($"Reset '{table.Key}' ({table.Value:N0} rows)? (y/n): ");
                if (string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    selected[table.Key] = table.Value;
                }
            }

            tables.Clear();
            foreach (var table in selected) tables[table.Key] = table.Value;
        }

        private static void EnsureBranchTable(NpgsqlConnection connection)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS public.branch (
                    branch_id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    branch_name text NOT NULL,
                    branch_number text NOT NULL UNIQUE,
                    mpesa_till_number text,
                    number_of_tills integer NOT NULL DEFAULT 1,
                    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";
            using (var command = new NpgsqlCommand(sql, connection)) command.ExecuteNonQuery();
        }

        private static void InsertBranchSetup(NpgsqlConnection connection, SireSetup setup)
        {
            const string sql = @"
                INSERT INTO public.branch (branch_name, branch_number, mpesa_till_number, number_of_tills)
                VALUES (@branchName, @branchNumber, @mpesaTill, @numberOfTills);";
            using (var command = new NpgsqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("branchName", setup.BranchName);
                command.Parameters.AddWithValue("branchNumber", setup.BranchNumber);
                command.Parameters.AddWithValue("mpesaTill", (object)setup.MpesaTillNumber ?? DBNull.Value);
                command.Parameters.AddWithValue("numberOfTills", setup.NumberOfTills);
                command.ExecuteNonQuery();
            }
        }

        private static void CleanTransactionalTables(NpgsqlConnection connection, Dictionary<string, long> tables)
        {
            if (tables.Count == 0) return;
            foreach (string table in tables.Keys)
            {
                int separator = table.IndexOf('.');
                string schema = table.Substring(0, separator);
                string name = table.Substring(separator + 1);
                string qualifiedTable = QuoteIdentifier(schema) + "." + QuoteIdentifier(name);
                try
                {
                    if (name.Equals("user_permission_details", StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteNonProtectedPermissionDetails(connection, schema, qualifiedTable);
                    }
                    else
                    {
                        string sql = $"TRUNCATE TABLE {qualifiedTable} RESTART IDENTITY;";
                        using (var command = new NpgsqlCommand(sql, connection)) command.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Reset {table}.");
                }
                catch (PostgresException ex) when (ex.SqlState == "P0001")
                {
                    Console.WriteLine($"Skipped {table}: the table is append-only and rejected the reset.");
                }
            }
        }

        private static void DeleteNonProtectedPermissionDetails(NpgsqlConnection connection, string schema, string qualifiedTable)
        {
            string columnName = null;
            const string columnSql = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = 'user_permission_details'
                  AND column_name IN ('branch_user_id', 'user_id')
                ORDER BY CASE column_name WHEN 'branch_user_id' THEN 1 ELSE 2 END
                LIMIT 1;";
            using (var columnCommand = new NpgsqlCommand(columnSql, connection))
            {
                columnCommand.Parameters.AddWithValue("schema", schema);
                columnName = columnCommand.ExecuteScalar() as string;
            }

            if (columnName == null)
            {
                Console.WriteLine($"Skipped {qualifiedTable}: no branch_user_id or user_id column was found.");
                return;
            }

            string deleteSql = $"DELETE FROM {qualifiedTable} WHERE {QuoteIdentifier(columnName)}::text <> @protectedUserId;";
            using (var deleteCommand = new NpgsqlCommand(deleteSql, connection))
            {
                deleteCommand.Parameters.AddWithValue("protectedUserId", "0099");
                deleteCommand.ExecuteNonQuery();
            }
        }

        private static void VacuumDatabase(NpgsqlConnection connection)
        {
            Console.WriteLine("Running VACUUM (FREEZE, ANALYZE, VERBOSE)...");
            connection.Notice += (sender, args) => Console.WriteLine(args.Notice.MessageText);
            using (var command = new NpgsqlCommand("VACUUM (FREEZE, ANALYZE, VERBOSE);", connection))
            {
                command.ExecuteNonQuery();
            }
            Console.WriteLine("Vacuum completed.");
        }

        public static List<string> GetDatabases(DbConfig config)
        {
            List<string> databases = new List<string>();
            try
            {
                string connString = $"Server={config.Server};Port={config.Port};Database=postgres;User Id={config.UserId};Password={config.Password};Timeout=10;";
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string sql = "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname;";
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                databases.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing databases: {ex.Message}");
            }
            return databases;
        }

        public static void ShowDatabases(DbConfig config)
        {
            var databases = GetDatabases(config);
            Console.WriteLine("\nAvailable databases:");
            for (int i = 0; i < databases.Count; i++)
            {
                Console.WriteLine($"{i + 1}: {databases[i]}");
            }
            Console.WriteLine("0: Go back to main menu");
            Console.WriteLine("\nPress any key to return...");
            Console.ReadKey();
        }

        public static void AccessDatabase(DbConfig config)
        {
            Console.Write($"Enter the name of the database to access [{config.Database}]: ");
            string dbName = Console.ReadLine();

            if (!string.IsNullOrEmpty(dbName))
            {
                config.Database = dbName;
            }

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
                var databases = GetDatabases(config);

                Console.WriteLine("\nAvailable databases:");
                for (int i = 0; i < databases.Count; i++)
                {
                    Console.WriteLine($"{i + 1}: {databases[i]}");
                }
                Console.WriteLine("0: Go back to main menu");

                Console.Write("Enter the number of the database you want to delete: ");
                string input = Console.ReadLine();
                if (input == "0") return;

                if (!int.TryParse(input, out int choice) || choice < 1 || choice > databases.Count)
                {
                    Console.WriteLine("Invalid choice. Operation canceled.");
                    return;
                }

                config.Database = databases[choice - 1];
                Console.WriteLine($"You have selected to delete the database '{config.Database}'.");

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
            string backupFilePath = null;
            try
            {
                Console.Write($"Enter the name of the database to backup [{config.Database}]: ");
                string dbName = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(dbName))
                {
                    dbName = config.Database;
                }

                if (string.IsNullOrWhiteSpace(dbName))
                {
                    Console.WriteLine("Error: Database name cannot be empty. Backup canceled.");
                    return;
                }

                string timestamp = DateTime.Now.ToString("yyMMddHHmmss");
                string backupFileName = $"{dbName}_{timestamp}.backup";

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                
                // Sanitize connection name for folder use
                string safeConfigName = string.Join("_", config.Name.Split(Path.GetInvalidFileNameChars()));
                string backupsFolderPath = Path.Combine(desktopPath, "backups", safeConfigName);

                if (!Directory.Exists(backupsFolderPath))
                {
                    Directory.CreateDirectory(backupsFolderPath);
                    Console.WriteLine($"Created backup folder: {backupsFolderPath}");
                }

                backupFilePath = Path.Combine(backupsFolderPath, backupFileName);
                Console.WriteLine($"Target backup file: {backupFilePath}");

                string dumpArguments = $"-h {config.Server} -U {config.UserId} -p {config.Port} -F c -b -v -f \"{backupFilePath}\" \"{dbName}\"";
                
                ProcessStartInfo procStartInfo = new ProcessStartInfo("pg_dump", dumpArguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                procStartInfo.EnvironmentVariables["PGPASSWORD"] = config.Password;

                Console.WriteLine("Starting the backup process...");
                using (var proc = new Process {StartInfo = procStartInfo})
                {
                    proc.OutputDataReceived += (sender, data) =>
                    {
                        if (!string.IsNullOrEmpty(data.Data)) Console.WriteLine(data.Data);
                    };
                    proc.ErrorDataReceived += (sender, data) =>
                    {
                        // pg_dump -v sends verbose info to stderr; we log it without [Error] prefix
                        if (!string.IsNullOrEmpty(data.Data)) Console.WriteLine(data.Data);
                    };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                    
                    if (proc.ExitCode == 0)
                    {
                        Console.WriteLine($"\nBackup successful! Saved to: {backupFilePath}");
                    }
                    else
                    {
                        Console.WriteLine($"\nBackup failed with exit code: {proc.ExitCode}");
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("Backup failed: 'pg_dump' was not found. Install PostgreSQL client tools and add the bin folder to PATH.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during backup: {ex.Message}");
            }
        }

        private static bool IsLocalServer(string server)
        {
            if (string.IsNullOrWhiteSpace(server)) return false;
            string s = server.Trim().ToLowerInvariant();
            return s == "localhost" || s == "127.0.0.1" || s == "::1" || s == "[::1]";
        }

        private static DbConfig SelectLocalTarget()
        {
            var localConfigs = LoadConfigs().Where(c => IsLocalServer(c.Server)).ToList();
            if (localConfigs.Count > 0)
            {
                Console.WriteLine("\nLocal target connections:");
                for (int i = 0; i < localConfigs.Count; i++)
                {
                    Console.WriteLine($"{i + 1}: {localConfigs[i].Name} ({localConfigs[i].Server}:{localConfigs[i].Port})");
                }
                Console.WriteLine("N: Enter a new local connection");
                Console.Write("Select the local target [1]: ");
                string input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) return localConfigs[0];
                if (int.TryParse(input, out int index) && index >= 1 && index <= localConfigs.Count)
                    return localConfigs[index - 1];
                if (!input.Equals("N", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Invalid choice.");
                    return null;
                }
            }

            var local = new DbConfig { Name = "Local", Server = "localhost", Database = "postgres", Port = 5432 };
            Console.Write("Local user ID [postgres]: ");
            string user = Console.ReadLine();
            local.UserId = string.IsNullOrWhiteSpace(user) ? "postgres" : user.Trim();
            Console.Write("Local password: ");
            local.Password = Console.ReadLine();
            local.Port = ReadPositiveInteger("Local port [5432]: ", 5432);

            if (!TestConnection(local)) return null;

            Console.Write("Save this as a connection for next time? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y")
            {
                var all = LoadConfigs();
                all.Add(local);
                SaveConfigs(all);
            }
            return local;
        }

        // Runs a CLI tool, streaming its output. Returns the exit code.
        private static int RunTool(string fileName, IEnumerable<string> arguments, DbConfig config,
            string extraPgOptions = null)
        {
            var info = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in arguments) info.ArgumentList.Add(arg);
            info.EnvironmentVariables["PGPASSWORD"] = config.Password;
            if (extraPgOptions != null) info.EnvironmentVariables["PGOPTIONS"] = extraPgOptions;

            using (var proc = new Process { StartInfo = info })
            {
                proc.OutputDataReceived += (s, d) => { if (!string.IsNullOrEmpty(d.Data)) Console.WriteLine(d.Data); };
                proc.ErrorDataReceived += (s, d) => { if (!string.IsNullOrEmpty(d.Data)) Console.WriteLine(d.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        private static bool LocalDatabaseExists(NpgsqlConnection adminConnection, string databaseName)
        {
            using (var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", adminConnection))
            {
                command.Parameters.AddWithValue("name", databaseName);
                return command.ExecuteScalar() != null;
            }
        }

        private static void DisconnectLocalUsers(NpgsqlConnection adminConnection, string databaseName)
        {
            const string sql = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                               "WHERE datname = @name AND pid <> pg_backend_pid()";
            using (var command = new NpgsqlCommand(sql, adminConnection))
            {
                command.Parameters.AddWithValue("name", databaseName);
                command.ExecuteNonQuery();
            }
        }

        // Pulls a database from a remote (non-local) server and restores it locally under the same name.
        // The remote is only ever read (pg_dump); every write happens on the local server.
        public static void PullRemoteToLocal(DbConfig remote)
        {
            try
            {
                if (IsLocalServer(remote.Server))
                {
                    Console.WriteLine("This option is only for remote connections. Use Backup/Import for local servers.");
                    return;
                }

                DbConfig local = SelectLocalTarget();
                if (local == null)
                {
                    Console.WriteLine("No local target selected. Operation canceled.");
                    return;
                }

                Console.Write($"Remote database to pull [{remote.Database}]: ");
                string dbName = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(dbName)) dbName = remote.Database;
                if (string.IsNullOrWhiteSpace(dbName) || dbName.Any(c => char.IsControl(c) || c == '"'))
                {
                    Console.WriteLine("Invalid database name. Operation canceled.");
                    return;
                }

                int jobs = ReadPositiveInteger(
                    $"Parallel jobs for dump/restore [{Math.Min(4, Environment.ProcessorCount)}] (each dump job opens a connection to the remote): ",
                    Math.Min(4, Environment.ProcessorCount));

                Console.WriteLine(
                    $"\nPull '{dbName}' from {remote.Server}:{remote.Port} (read-only) into {local.Server}:{local.Port} as '{dbName}'.");
                Console.Write("Continue? (yes/no): ");
                if (Console.ReadLine()?.Trim().ToLowerInvariant() != "yes")
                {
                    Console.WriteLine("Canceled.");
                    return;
                }

                // 1. Dump the remote first, so the local database is untouched if the dump fails.
                // Directory format allows parallel dump and parallel restore; -Z 3 trades a little size for speed.
                string safeConfigName = string.Join("_", remote.Name.Split(Path.GetInvalidFileNameChars()));
                string dumpDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "backups", safeConfigName,
                    $"{dbName}_{DateTime.Now:yyMMddHHmmss}.dumpdir");
                Directory.CreateDirectory(Path.GetDirectoryName(dumpDir));

                Console.WriteLine($"\nDumping remote database to {dumpDir} ...");
                var stopwatch = Stopwatch.StartNew();
                int dumpExit = RunTool("pg_dump", new[]
                {
                    "-h", remote.Server, "-p", remote.Port.ToString(), "-U", remote.UserId,
                    "-F", "d", "-j", jobs.ToString(), "-Z", "3", "--no-owner", "--no-acl", "-v",
                    "-f", dumpDir, dbName
                }, remote);
                if (dumpExit != 0)
                {
                    Console.WriteLine($"\nDump failed with exit code {dumpExit}. Local server was not touched.");
                    return;
                }
                Console.WriteLine($"Dump completed in {stopwatch.Elapsed:hh\\:mm\\:ss}.");

                // 2. Prepare the local server: rename or drop an existing database with the same name.
                string adminConnectionString =
                    $"Server={local.Server};Port={local.Port};Database=postgres;User Id={local.UserId};Password={local.Password};Timeout=30;";
                using (var admin = new NpgsqlConnection(adminConnectionString))
                {
                    admin.Open();

                    if (LocalDatabaseExists(admin, dbName))
                    {
                        Console.WriteLine($"\nLocal database '{dbName}' already exists.");
                        Console.Write("[R]ename it to a timestamped name (default) or [D]rop it? (R/D): ");
                        string choice = Console.ReadLine()?.Trim().ToUpperInvariant();

                        DisconnectLocalUsers(admin, dbName);
                        if (choice == "D")
                        {
                            Console.Write($"Type the database name '{dbName}' to confirm the drop: ");
                            if (Console.ReadLine()?.Trim() != dbName)
                            {
                                Console.WriteLine($"Confirmation did not match. Canceled; the dump is kept at {dumpDir}.");
                                return;
                            }
                            using (var drop = new NpgsqlCommand($"DROP DATABASE {QuoteIdentifier(dbName)}", admin))
                            {
                                drop.ExecuteNonQuery();
                            }
                            Console.WriteLine($"Dropped local database '{dbName}'.");
                        }
                        else
                        {
                            string renamed = $"{dbName}_{DateTime.Now:yyMMddHHmmss}";
                            using (var rename = new NpgsqlCommand(
                                $"ALTER DATABASE {QuoteIdentifier(dbName)} RENAME TO {QuoteIdentifier(renamed)}", admin))
                            {
                                rename.ExecuteNonQuery();
                            }
                            Console.WriteLine($"Renamed existing local database to '{renamed}'.");
                        }
                    }

                    using (var create = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(dbName)}", admin))
                    {
                        create.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Created local database '{dbName}'.");
                }

                // 3. Restore in parallel. synchronous_commit=off is safe here: the database is brand new and
                // can be rebuilt from the dump, and it avoids a WAL flush per transaction.
                Console.WriteLine("\nRestoring locally ...");
                stopwatch.Restart();
                int restoreExit = RunTool("pg_restore", new[]
                {
                    "-h", local.Server, "-p", local.Port.ToString(), "-U", local.UserId,
                    "-d", dbName, "-j", jobs.ToString(), "--no-owner", "--no-acl", "-v", dumpDir
                }, local, "-c synchronous_commit=off");

                Console.WriteLine($"\nRestore finished in {stopwatch.Elapsed:hh\\:mm\\:ss} with exit code {restoreExit}.");
                if (restoreExit != 0)
                {
                    Console.WriteLine("pg_restore reported errors (often missing extensions or roles). Review the output above.");
                }
                Console.WriteLine($"Local database '{dbName}' is ready on {local.Server}:{local.Port}.");

                Console.Write($"Delete the dump at {dumpDir}? (y/n) [n]: ");
                if (Console.ReadLine()?.Trim().ToLowerInvariant() == "y")
                {
                    Directory.Delete(dumpDir, true);
                    Console.WriteLine("Dump deleted.");
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("'pg_dump' or 'pg_restore' was not found. Install PostgreSQL client tools and add the bin folder to PATH.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while pulling the remote database: {ex.Message}");
            }
        }


    }
}
