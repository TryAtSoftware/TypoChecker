namespace API;

using API.Settings;
using Core.Features;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
        builder.Services.Configure<DocumentIntelligenceSettings>(builder.Configuration.GetSection(DocumentIntelligenceSettings.Section));

        builder.Services.AddSingleton<IWordsRegister, SetWordsRegister>();

        builder.Services.AddControllers();
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.MapControllers();

        var wordsRegister = app.Services.GetRequiredService<IWordsRegister>();
        await LoadWordsAsync(wordsRegister, "Slovored/forms-words-list.txt");
        
        await app.RunAsync();
    }

    private static async Task LoadWordsAsync(IWordsRegister wordsRegister, string fileName)
    {
        var pathToFile = Path.Combine(AppContext.BaseDirectory, fileName);
        var words = await File.ReadAllLinesAsync(pathToFile);

        foreach (var word in words) wordsRegister.Register(word);
    }
}