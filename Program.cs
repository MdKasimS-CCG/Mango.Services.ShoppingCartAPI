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

//TODO: establish network comms between containers.
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<ServiceUrlsOptions>(
    builder.Configuration.GetSection("ServiceUrls"));

builder.Services.Configure<ApiSettingsOptions>(
    builder.Configuration.GetSection("ApiSettings"));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
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
    var serviceUrls = serviceProvider
        .GetRequiredService<IOptions<ServiceUrlsOptions>>()
        .Value;

    client.BaseAddress = new Uri(serviceUrls.ProductAPI);
})
.AddHttpMessageHandler<BackendApiAuthenticationHttpClientHandler>();

builder.Services.AddHttpClient("Coupon", (serviceProvider, client) =>
{
    var serviceUrls = serviceProvider
        .GetRequiredService<IOptions<ServiceUrlsOptions>>()
        .Value;

    client.BaseAddress = new Uri(serviceUrls.CouponAPI);
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
        var settings = builder.Configuration
            .GetSection("ApiSettings")
            .Get<ApiSettingsOptions>()!;

        var key = Encoding.ASCII.GetBytes(settings.Secret);

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(key),

                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,

                ValidateAudience = true,
                ValidAudience = settings.Audience
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

public class ServiceUrlsOptions
{
    public string ProductAPI { get; set; } = string.Empty;
    public string CouponAPI { get; set; } = string.Empty;
}

public class ApiSettingsOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
