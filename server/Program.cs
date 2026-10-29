using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

var dbPath = Path.Combine(AppContext.BaseDirectory, "skillbridge.db");
var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
await InitializeDatabase();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "SkillBridge API" }));
app.MapGet("/api/skills", async (string? search, string? category) =>
{
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    var sql = "SELECT id,title,category,description,level,sessions,mentor,initials,color,icon FROM skills WHERE (@search IS NULL OR title LIKE @pattern OR description LIKE @pattern OR mentor LIKE @pattern) AND (@category IS NULL OR @category='All skills' OR category=@category) ORDER BY id";
    await using var cmd = new SqliteCommand(sql, db);
    cmd.Parameters.AddWithValue("@search", string.IsNullOrWhiteSpace(search) ? DBNull.Value : search);
    cmd.Parameters.AddWithValue("@pattern", $"%{search ?? ""}%");
    cmd.Parameters.AddWithValue("@category", string.IsNullOrWhiteSpace(category) ? DBNull.Value : category);
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>();
    while (await reader.ReadAsync()) rows.Add(ReadSkill(reader));
    return Results.Ok(rows);
});
app.MapGet("/api/skills/{id:int}", async (int id) =>
{
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    await using var cmd = new SqliteCommand("SELECT id,title,category,description,level,sessions,mentor,initials,color,icon FROM skills WHERE id=@id", db); cmd.Parameters.AddWithValue("@id", id);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.NotFound(new { message = "Skill not found." });
    return Results.Ok(ReadSkill(reader));
});
app.MapPost("/api/auth/register", async (RegisterRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)) return Results.BadRequest(new { message = "Please complete all fields." });
    if (request.Password.Length < 6) return Results.BadRequest(new { message = "Password must be at least 6 characters." });
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    var salt = RandomNumberGenerator.GetBytes(16); var hash = HashPassword(request.Password, salt);
    try
    {
        await using var cmd = new SqliteCommand("INSERT INTO users(name,email,password_hash,password_salt,created_at) VALUES(@name,@email,@hash,@salt,@created)", db);
        cmd.Parameters.AddWithValue("@name", request.Name.Trim()); cmd.Parameters.AddWithValue("@email", request.Email.Trim().ToLowerInvariant()); cmd.Parameters.AddWithValue("@hash", Convert.ToBase64String(hash)); cmd.Parameters.AddWithValue("@salt", Convert.ToBase64String(salt)); cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        return Results.Ok(new { name = request.Name.Trim(), email = request.Email.Trim().ToLowerInvariant(), message = "Account created." });
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return Results.Conflict(new { message = "An account with this email already exists." }); }
});
app.MapPost("/api/auth/login", async (LoginRequest request) =>
{
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    await using var cmd = new SqliteCommand("SELECT name,password_hash,password_salt FROM users WHERE email=@email", db); cmd.Parameters.AddWithValue("@email", request.Email.Trim().ToLowerInvariant());
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return Results.Json(new { message = "Email or password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);
    var name = reader.GetString(0); var storedHash = Convert.FromBase64String(reader.GetString(1)); var salt = Convert.FromBase64String(reader.GetString(2));
    var submittedHash = HashPassword(request.Password, salt);
    if (!CryptographicOperations.FixedTimeEquals(storedHash, submittedHash)) return Results.Json(new { message = "Email or password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);
    return Results.Ok(new { name, email = request.Email.Trim().ToLowerInvariant(), message = "Logged in." });
});
app.MapPost("/api/bookings", async (BookingRequest request) =>
{
    if (request.SkillId <= 0 || string.IsNullOrWhiteSpace(request.LearnerName)) return Results.BadRequest(new { message = "Please sign in and choose a skill." });
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    await using var exists = new SqliteCommand("SELECT COUNT(*) FROM skills WHERE id=@id", db); exists.Parameters.AddWithValue("@id", request.SkillId);
    if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0) return Results.NotFound(new { message = "Skill not found." });
    await using var cmd = new SqliteCommand("INSERT INTO bookings(skill_id,learner_name,mentor_name,status,created_at) VALUES(@skill,@learner,@mentor,'Requested',@created); SELECT last_insert_rowid();", db);
    cmd.Parameters.AddWithValue("@skill", request.SkillId); cmd.Parameters.AddWithValue("@learner", request.LearnerName.Trim()); cmd.Parameters.AddWithValue("@mentor", request.MentorName ?? "SkillBridge mentor"); cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("O"));
    var id = Convert.ToInt64(await cmd.ExecuteScalarAsync()); return Results.Created($"/api/bookings/{id}", new { id, status = "Requested", message = "Your session request has been saved." });
});
app.MapGet("/api/bookings", async (string? learner) =>
{
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    await using var cmd = new SqliteCommand("SELECT b.id,b.skill_id,s.title,b.learner_name,b.mentor_name,b.status,b.created_at FROM bookings b JOIN skills s ON s.id=b.skill_id WHERE (@learner IS NULL OR b.learner_name=@learner) ORDER BY b.id DESC", db);
    cmd.Parameters.AddWithValue("@learner", string.IsNullOrWhiteSpace(learner) ? DBNull.Value : learner);
    await using var reader = await cmd.ExecuteReaderAsync(); var rows = new List<object>();
    while (await reader.ReadAsync()) rows.Add(new { id = reader.GetInt64(0), skillId = reader.GetInt32(1), skillTitle = reader.GetString(2), learnerName = reader.GetString(3), mentorName = reader.GetString(4), status = reader.GetString(5), createdAt = reader.GetString(6) });
    return Results.Ok(rows);
});
app.Run();

async Task InitializeDatabase()
{
    await using var db = new SqliteConnection(connectionString); await db.OpenAsync();
    var schema = @"
CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,email TEXT NOT NULL UNIQUE,password_hash TEXT NOT NULL,password_salt TEXT NOT NULL,created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS skills(id INTEGER PRIMARY KEY,title TEXT NOT NULL,category TEXT NOT NULL,description TEXT NOT NULL,level TEXT NOT NULL,sessions INTEGER NOT NULL,mentor TEXT NOT NULL,initials TEXT NOT NULL,color TEXT NOT NULL,icon TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS bookings(id INTEGER PRIMARY KEY AUTOINCREMENT,skill_id INTEGER NOT NULL,learner_name TEXT NOT NULL,mentor_name TEXT NOT NULL,status TEXT NOT NULL,created_at TEXT NOT NULL,FOREIGN KEY(skill_id) REFERENCES skills(id));";
    await using (var cmd = new SqliteCommand(schema, db)) await cmd.ExecuteNonQueryAsync();
    await using var count = new SqliteCommand("SELECT COUNT(*) FROM skills", db);
    if (Convert.ToInt32(await count.ExecuteScalarAsync()) == 0)
    {
        var seeds = new[] {
            new SkillSeed(1,"React Development","Development","Build modern, interactive web applications with confidence.","Intermediate",4,"Mariam Hassan","MH","purple","react"),
            new SkillSeed(2,"UI/UX Design","Design","Turn your ideas into intuitive and delightful user experiences.","Beginner friendly",3,"Omar Adel","OA","peach","design"),
            new SkillSeed(3,"Python Programming","Data & AI","Explore programming, automation, and the world of data.","All levels",5,"Nour Samir","NS","green","python"),
            new SkillSeed(4,"Product Thinking","Business","Learn how to turn a real problem into a useful product.","Beginner friendly",2,"Youssef Ali","YA","blue","product"),
            new SkillSeed(5,"Public Speaking","Personal growth","Share your ideas clearly and speak with more confidence.","All levels",3,"Salma Nabil","SN","pink","speak"),
            new SkillSeed(6,"Data Analysis","Data & AI","Find patterns, tell stories, and make better decisions with data.","Intermediate",4,"Khaled Mostafa","KM","yellow","data")
        };
        foreach (var s in seeds) { await using var cmd = new SqliteCommand("INSERT INTO skills(id,title,category,description,level,sessions,mentor,initials,color,icon) VALUES(@id,@title,@category,@description,@level,@sessions,@mentor,@initials,@color,@icon)", db); cmd.Parameters.AddWithValue("@id",s.Id);cmd.Parameters.AddWithValue("@title",s.Title);cmd.Parameters.AddWithValue("@category",s.Category);cmd.Parameters.AddWithValue("@description",s.Description);cmd.Parameters.AddWithValue("@level",s.Level);cmd.Parameters.AddWithValue("@sessions",s.Sessions);cmd.Parameters.AddWithValue("@mentor",s.Mentor);cmd.Parameters.AddWithValue("@initials",s.Initials);cmd.Parameters.AddWithValue("@color",s.Color);cmd.Parameters.AddWithValue("@icon",s.Icon);await cmd.ExecuteNonQueryAsync(); }
    }
}
static byte[] HashPassword(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 120_000, HashAlgorithmName.SHA256, 32);
static object ReadSkill(SqliteDataReader r) => new { id=r.GetInt32(0),title=r.GetString(1),category=r.GetString(2),description=r.GetString(3),level=r.GetString(4),sessions=r.GetInt32(5),mentor=r.GetString(6),initials=r.GetString(7),color=r.GetString(8),icon=r.GetString(9) };
record RegisterRequest(string Name,string Email,string Password);
record LoginRequest(string Email,string Password);
record BookingRequest(int SkillId,string LearnerName,string? MentorName);
record SkillSeed(int Id,string Title,string Category,string Description,string Level,int Sessions,string Mentor,string Initials,string Color,string Icon);
