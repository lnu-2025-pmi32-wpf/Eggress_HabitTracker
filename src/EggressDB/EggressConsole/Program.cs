using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

internal class Program
{
    private const string ConnString =
        "Host=localhost;Port=5432;Database=eggress;Username=postgres;Password=postgres";

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Help();
            return 1;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "init":
                    InitSchema();
                    Console.WriteLine("OK: schema created.");
                    break;

                case "seed":
                    Seed(30, 50);
                    Console.WriteLine("OK: seeded random data.");
                    break;

                case "print":
                    PrintData();
                    break;

                default:
                    Help();
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    static void Help()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  init  - execute db_init.sql (create tables)");
        Console.WriteLine("  seed  - insert 30-50 random rows into each table");
        Console.WriteLine("  print - print sample rows from tables");
    }

    static void InitSchema()
    {
        var sqlPath = Path.Combine(AppContext.BaseDirectory, "db_init.sql");

        if (!File.Exists(sqlPath))
            sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "db_init.sql");

        if (!File.Exists(sqlPath))
            throw new FileNotFoundException("db_init.sql not found at: " + sqlPath);

        var sql = File.ReadAllText(sqlPath);

        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    static void Seed(int min, int max)
    {
        var rnd = new Random();

        int usersCount = rnd.Next(min, max + 1);
        int habitsCount = rnd.Next(min, max + 1);
        int logsCount = rnd.Next(min, max + 1);

        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();

        using (var clean = new NpgsqlCommand(@"
            TRUNCATE TABLE habit_logs RESTART IDENTITY;
            TRUNCATE TABLE habits RESTART IDENTITY CASCADE;
            TRUNCATE TABLE users RESTART IDENTITY CASCADE;
        ", conn))
        {
            clean.ExecuteNonQuery();
        }

        for (int i = 1; i <= usersCount; i++)
        {
            string login = $"user_{i:000}";
            string email = $"user_{i:000}@mail.com";
            string passHash = Sha256Hex($"password{i:000}");
            bool theme = rnd.Next(0, 2) == 1;

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO users(login, email, password_hash, theme_preference, created_at)
                VALUES (@login, @email, @ph, @theme, NOW());
            ", conn);

            cmd.Parameters.AddWithValue("login", login);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("ph", passHash);
            cmd.Parameters.AddWithValue("theme", theme);
            cmd.ExecuteNonQuery();
        }

        int[] userIds = ReadIntColumn(conn, "SELECT user_id FROM users ORDER BY user_id;");

        string[] titles = { "Читання", "Спорт", "Вода", "Медитація", "Кодинг", "Сон", "Англійська", "Прибирання" };
        int[] periods = { 1, 7, 30 };

        for (int i = 1; i <= habitsCount; i++)
        {
            int userId = userIds[rnd.Next(userIds.Length)];
            string title = $"{titles[rnd.Next(titles.Length)]} #{i}";
            string desc = $"random description {i}";
            int period = periods[rnd.Next(periods.Length)];
            int points = rnd.Next(0, 61);
            string hex = RandomHexColor(rnd);
            DateTime lastCheckIn = DateTime.UtcNow.AddDays(-rnd.Next(0, 40));
            bool hatched = points >= 60;

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO habits(user_id, title, description, period_n, current_points, egg_color_hex, last_check_in, is_hatched)
                VALUES (@uid, @t, @d, @p, @cp, @hex, @lci, @hatched);
            ", conn);

            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("t", title);
            cmd.Parameters.AddWithValue("d", desc);
            cmd.Parameters.AddWithValue("p", period);
            cmd.Parameters.AddWithValue("cp", points);
            cmd.Parameters.AddWithValue("hex", hex);
            cmd.Parameters.AddWithValue("lci", lastCheckIn);
            cmd.Parameters.AddWithValue("hatched", hatched);
            cmd.ExecuteNonQuery();
        }

        int[] habitIds = ReadIntColumn(conn, "SELECT habit_id FROM habits ORDER BY habit_id;");

        int[] deltas = { 1, 7, 30, -1, -7, -30 };

        for (int i = 1; i <= logsCount; i++)
        {
            int habitId = habitIds[rnd.Next(habitIds.Length)];
            int delta = deltas[rnd.Next(deltas.Length)];
            DateTime ts = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 60 * 24 * 30));

            using var cmd = new NpgsqlCommand(@"
                INSERT INTO habit_logs(habit_id, points_added, log_timestamp)
                VALUES (@hid, @delta, @ts);
            ", conn);

            cmd.Parameters.AddWithValue("hid", habitId);
            cmd.Parameters.AddWithValue("delta", delta);
            cmd.Parameters.AddWithValue("ts", ts);
            cmd.ExecuteNonQuery();
        }

        using var check = new NpgsqlCommand(@"
            SELECT 'users' t, COUNT(*) c FROM users
            UNION ALL SELECT 'habits', COUNT(*) FROM habits
            UNION ALL SELECT 'habit_logs', COUNT(*) FROM habit_logs;
        ", conn);

        using var r = check.ExecuteReader();
        Console.WriteLine("Row counts:");
        while (r.Read())
            Console.WriteLine($"{r.GetString(0)}: {r.GetInt64(1)}");
    }

    static void PrintData()
    {
        using var conn = new NpgsqlConnection(ConnString);
        conn.Open();

        Console.WriteLine("=== USERS (first 10) ===");
        PrintQuery(conn, "SELECT user_id, login, email, theme_preference, created_at FROM users ORDER BY user_id LIMIT 10;");

        Console.WriteLine("\n=== HABITS (first 10) ===");
        PrintQuery(conn, "SELECT habit_id, user_id, title, period_n, current_points, egg_color_hex, last_check_in, is_hatched FROM habits ORDER BY habit_id LIMIT 10;");

        Console.WriteLine("\n=== HABIT_LOGS (first 10) ===");
        PrintQuery(conn, "SELECT log_id, habit_id, points_added, log_timestamp FROM habit_logs ORDER BY log_id LIMIT 10;");
    }

    static void PrintQuery(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();

        var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        Console.WriteLine(string.Join(" | ", cols));

        while (r.Read())
        {
            var vals = new object[r.FieldCount];
            r.GetValues(vals);
            Console.WriteLine(string.Join(" | ", vals.Select(v => v is DBNull ? "NULL" : v)));
        }
    }

    static int[] ReadIntColumn(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        using var r = cmd.ExecuteReader();
        var list = new System.Collections.Generic.List<int>();
        while (r.Read()) list.Add(r.GetInt32(0));
        return list.ToArray();
    }

    static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    static string RandomHexColor(Random rnd)
    {
        int r = rnd.Next(0, 256);
        int g = rnd.Next(0, 256);
        int b = rnd.Next(0, 256);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
