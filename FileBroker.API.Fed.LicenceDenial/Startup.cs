﻿using FileBroker.Common;
using FOAEA3.Common.Filters;
using FOAEA3.Model;
using FOAEA3.Resources.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

namespace FileBroker.API.Fed.LicenceDenial
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
                options.Filters.Add(new ActionAutoLoggerFilter());
            });
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "FileBroker.API.Fed.LicenceDenial", Version = "v1" });
            });

            string fileBrokerCON = Configuration.GetConnectionString("FileBroker").ReplaceVariablesWithEnvironmentValues();

            ColourConsole.WriteEmbeddedColorLine($"Starting [cyan]FileBroker.API.Fed.LicenceDenial[/cyan]...");

            string actualConnection = DataHelper.ConfigureDBServices(services, fileBrokerCON);

            ColourConsole.WriteEmbeddedColorLine($"Using Connection: [yellow]{actualConnection}[/yellow]");

            services.Configure<ApiConfig>(Configuration.GetSection("APIroot"));

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FileBroker.API.Fed.LicenceDenial v1"));
            }

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            var api_url = Configuration["Urls"];

            ColourConsole.WriteEmbeddedColorLine($"Using .Net Code Environment = [yellow]{env.EnvironmentName}[/yellow]");
            ColourConsole.WriteLine($"");
            ColourConsole.WriteEmbeddedColorLine($"[green]Waiting for API calls...[/green] [yellow]{api_url}[/yellow]");
            ColourConsole.WriteLine($"");
        }

    }
}