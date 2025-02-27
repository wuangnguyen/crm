using CRM.Shared;
using CRM.Shared.EventBus;
using CRM.Shared.EventBus.Nats;
using CRM.Shared.Interceptors;
using CRM.Shared.Jaeger;
using CRM.Shared.Repository;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Npgsql;
using OpenTracing.Contrib.Grpc.Interceptors;
using CRM.IntegrationEvents;
using MediatR;
using CRM.Contact.IntegrationHandlers;
using CRM.Contact.Services;
using CRM.Contact.Extensions;

namespace CRM.Contact
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            IdentityModelEventSource.ShowPII = true;
            Configuration = configuration;

            SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL);
        }

        private IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<NatsOptions>(Configuration.GetSection("NATS"));
            services.AddJaeger();

            RegisterGrpc(services);
            // RegisterAuth(services);

            services.Scan(scan => scan
                .FromCallingAssembly()
                .AddClasses(c => c.AssignableTo(typeof(IValidator<>)))
                .AsImplementedInterfaces()
                .WithTransientLifetime());

            RegisterRepository(services);

            RegisterServiceBus(services);

            services.AddMediatR(typeof(Startup));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            // app.UseAuthentication();
            // app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // Communication with gRPC endpoints must be made through a gRPC client.
                // To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909
                endpoints.MapGrpcService<LeadService>();
                endpoints.MapGrpcService<ContactService>();
            });

            var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();
            var appOptions = Configuration.GetOptions<AppOptions>("App");
            eventBus.Subscribe<ContactCreatedEvent, ContactCreatedEventHandler>(appOptions.Name);
        }

        private void RegisterRepository(IServiceCollection services)
        {
            services.AddScoped<IUnitOfWork>(sp =>
            {
                return new UnitOfWork(() => new NpgsqlConnection(Configuration.GetConnectionString("contact")));
            });
        }

        private static void RegisterGrpc(IServiceCollection services)
        {
            services.AddGrpc(options =>
            {
                options.Interceptors.Add<ExceptionInterceptor>();
                options.Interceptors.Add<ServerTracingInterceptor>();
                options.EnableDetailedErrors = true;
            });
        }

        private static void RegisterServiceBus(IServiceCollection services)
        {
            services.AddSingleton<INatsConnection, DefaultNatsConnection>();
            services.AddSingleton<IEventBus, EventBusNats>();
            services.AddSingleton<IEventBusSubscriptionsManager, InMemoryEventBusSubscriptionsManager>();

            services.AddTransient<ContactCreatedEventHandler>();
        }

        private static void RegisterAuth(IServiceCollection services)
        {
            services.AddAuthorization();
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer((options) =>
                {
                    options.Authority = "http://localhost:8080/auth/realms/master";
                    options.RequireHttpsMetadata = false;
                    options.Audience = "account";
                });
        }
    }
}
