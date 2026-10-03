using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#region Configuração do builder

var builder = WebApplication.CreateSlimBuilder(args);

var applicationInsightsConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"];

if (string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    applicationInsightsConnectionString =
        builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
}

#endregion

#region Registro de serviços

builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = applicationInsightsConnectionString;
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("MinhaPoliticaCors", policy =>
    {
        policy.WithOrigins("https://techstorefront.z15.web.core.windows.net") 
            .AllowAnyMethod()                         
            .AllowAnyHeader();                         
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string não encontrada.");

builder.Services
    .AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy(), tags: ["api"])
    .AddAsyncCheck("database", async cancellationToken =>
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }, tags: ["database"]);

#endregion

#region Construção da aplicação

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

#endregion

#region Health checks

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/db", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("database")
});

#endregion

#region Endpoints de produtos

app.MapGet("/api/produtos", async () =>
{
    var produtos = new List<Produto>();
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand("SELECT Id, Nome, Preco, Estoque FROM Produtos", conn);
    await using var reader = await cmd.ExecuteReaderAsync();
    
    while (await reader.ReadAsync())
    {
        produtos.Add(new Produto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetInt32(3)
        ));
    }

    return Results.Ok(produtos);
});


app.MapPost("/api/produtos", async (Produto produto) =>
{
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = "INSERT INTO Produtos (Nome, Preco, Estoque) VALUES (@Nome, @Preco, @Estoque)";
    var cmd = new SqlCommand(sql, conn);
    
    cmd.Parameters.AddWithValue("@Nome", produto.Nome);
    cmd.Parameters.AddWithValue("@Preco", produto.Preco);
    cmd.Parameters.AddWithValue("@Estoque", produto.Estoque);

    await cmd.ExecuteNonQueryAsync();

    return Results.Created($"/produtos/{produto.Id}", produto);
});

app.MapDelete("/api/produtos/{id}", async (int id) =>
{
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var sql = "DELETE FROM Produtos WHERE Id = @Id";
    var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@Id", id);

    var rowsAffected = await cmd.ExecuteNonQueryAsync();
    if (rowsAffected == 0)
    {
        return Results.NotFound();
    }

    return Results.NoContent();
});

#endregion

#region Middleware

app.UseCors("MinhaPoliticaCors");

app.Run();

#endregion

#region Models

public record Produto(
    int Id,
    string Nome,
    decimal Preco,
    int Estoque);

#endregion
