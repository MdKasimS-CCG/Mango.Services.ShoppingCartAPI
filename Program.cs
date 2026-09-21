using AutoMapper;

using Mango.MessageBus;
using Mango.Services.ShoppingCartAPI;
using Mango.Services.ShoppingCartAPI.Data;
using Mango.Services.ShoppingCartAPI.Extensions;
using Mango.Services.ShoppingCartAPI.Service;
using Mango.Services.ShoppingCartAPI.Service.IService;
using Mango.Services.ShoppingCartAPI.Utility;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Options;
using DotNetEnv;

// Determine whether the application is running inside a Docker container.
bool isRunningInContainer =
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

// Load the .env file before creating the WebApplicationBuilder
// when running through the HTTP/local profile.
if (!isRunningInContainer)
{
    Env.Load();
}

var builder = WebApplication.CreateBuilder(args);

// Select the configuration section for the current execution mode.
string configurationPrefix = isRunningInContainer
    ? "Docker"
    : "Http";

// Create the single options object used by ShoppingCartAPI.
var mangoOptions = new MangoOptions
{
    Secret =
        builder.Configuration["ApiSettings:Secret"]
        ?? string.Empty,

    Issuer =
        builder.Configuration["ApiSettings:Issuer"]
        ?? string.Empty,

    Audience =
        builder.Configuration["ApiSettings:Audience"]
        ?? string.Empty,

    DefaultConnection =
        builder.Configuration[
            $"{configurationPrefix}:ConnectionStrings:DefaultConnection"]
        ?? string.Empty,

    ProductAPI =
        builder.Configuration[
            $"{configurationPrefix}:ServiceUrls:ProductAPI"]
        ?? string.Empty,

    CouponAPI =
        builder.Configuration[
            $"{configurationPrefix}:ServiceUrls:CouponAPI"]
        ?? string.Empty,

    EmailShoppingCartQueue =
        builder.Configuration[
            "TopicAndQueueNames:EmailShoppingCartQueue"]
        ?? string.Empty
};

// Register the single options object with dependency injection.
builder.Services.AddSingleton(
    Microsoft.Extensions.Options.Options.Create(mangoOptions));

// Make the selected values available through the normal
// ASP.NET Core configuration hierarchy as well.
builder.Configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] =
            mangoOptions.DefaultConnection,

        ["ServiceUrls:ProductAPI"] =
            mangoOptions.ProductAPI,

        ["ServiceUrls:CouponAPI"] =
            mangoOptions.CouponAPI,

        ["TopicAndQueueNames:EmailShoppingCartQueue"] =
            mangoOptions.EmailShoppingCartQueue,

        ["ApiSettings:Secret"] =
            mangoOptions.Secret,

        ["ApiSettings:Issuer"] =
            mangoOptions.Issuer,

        ["ApiSettings:Audience"] =
            mangoOptions.Audience
    });

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<MangoOptions>>()
        .Value;

    options.UseSqlServer(settings.DefaultConnection);
});

IMapper mapper = MappingConfig.RegisterMaps().CreateMapper();
builder.Services.AddSingleton(mapper);
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BackendApiAuthenticationHttpClientHandler>();
builder.Services.AddScoped<ICouponService, CouponService>();
// Note: Do remember, this is unlike tutor's Azure Service Bus. It uses RabbitMQ implementation in main branch itself.
builder.Services.AddScoped<IMessageProducer, RabbitMQMessageProducer>();

/// Another way to configure Httpclient service.
/// The other way is shown in Web project
/// Later, BackednApiAuthentcaitionHttpClientHandler was added.
/// May be to acheive this in simple way, this way Httpclient was configured
builder.Services.AddHttpClient("Product", (serviceProvider, client) =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<MangoOptions>>()
        .Value;

    client.BaseAddress = new Uri(settings.ProductAPI);
})
.AddHttpMessageHandler<BackendApiAuthenticationHttpClientHandler>();

builder.Services.AddHttpClient("Coupon", (serviceProvider, client) =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<MangoOptions>>()
        .Value;

    client.BaseAddress = new Uri(settings.CouponAPI);
})
.AddHttpMessageHandler<BackendApiAuthenticationHttpClientHandler>();


builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(option =>
{
    option.AddSecurityDefinition(name: JwtBearerDefaults.AuthenticationScheme, securityScheme: new OpenApiSecurityScheme()
    {
        //This name is shown in Authorize pop up of CouponAPI Swagger
        Name = "Authorization",
        Description = "Enter the Bearer Authorization string as following: `Bearer Generated-JWT-Token`",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    option.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {Type= ReferenceType.SecurityScheme,
                Id=JwtBearerDefaults.AuthenticationScheme}
            }, new string[] {} //TODO: Why this is empty here
        }
    });
});

//Adding Authentication
//builder.AddAppAuthentication();
//builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var key = Encoding.ASCII.GetBytes(mangoOptions.Secret);

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(key),

                ValidateIssuer = true,
                ValidIssuer = mangoOptions.Issuer,

                ValidateAudience = true,
                ValidAudience = mangoOptions.Audience
            };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

ApplyMigration();

app.Run();


void ApplyMigration()
{
    using (var scope = app.Services.CreateScope())
    {
        var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (_db.Database.GetPendingMigrations().Count() > 0)
        {
            _db.Database.Migrate();
        }
    }
}

public class MangoOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    public string DefaultConnection { get; set; } = string.Empty;

    public string ProductAPI { get; set; } = string.Empty;
    public string CouponAPI { get; set; } = string.Empty;

    public string EmailShoppingCartQueue { get; set; } = string.Empty;
}