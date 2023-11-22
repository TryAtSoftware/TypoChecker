
using API.Features;
using API.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);
            builder.Services.Configure<DocumentIntelligenceSettings>(builder.Configuration.GetSection(DocumentIntelligenceSettings.Section));

            builder.Services.AddSingleton<IWordsRegister, WordsRegister>();

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

            //app.UseAuthorization();


            app.MapControllers();
            var wordsRegister = app.Services.GetRequiredService<IWordsRegister>();
            await wordsRegister.InitializeAsync(app.Lifetime.ApplicationStopping);
            await app.RunAsync();
        }
    }
}
