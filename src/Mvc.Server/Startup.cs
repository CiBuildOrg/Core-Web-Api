using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mvc.Server.Models;
using OpenIddict.Core;
using OpenIddict.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Mvc.Server.Filters;
using Mvc.Server.Helpers;
using Serilog;
using Swashbuckle.AspNetCore.Swagger;

namespace Mvc.Server
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; set; }

        public Startup(IHostingEnvironment env)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("config.json", false, true)
                .AddJsonFile($"config.{env.EnvironmentName.ToLower()}.json", true)
                .AddEnvironmentVariables();
            Configuration = configuration.Build();
        }

        public void ConfigureServices(IServiceCollection services)
        {

            // Add Swagger generator
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new Info { Title = "My Web API", Version = "v1" });
            });

            // Add MVC Core
            services.AddMvcCore(
                    options =>
                    {
                        // Add global authorization filter 
                        var policy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build();

                        options.Filters.Add(new ApplicationAuthorizeFilter(policy));

                        // Add global exception handler for production
                        options.Filters.Add(typeof(CustomExceptionFilterAttribute));

                        // Add global validation filter
                        options.Filters.Add(typeof(ValidateModelFilterAttribute));

                    }
                )
                .AddJsonFormatters()
                .AddAuthorization()
                .AddDataAnnotations()
                .AddCors()
                .AddApiExplorer();
            services.AddMvc();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                // Configure the context to use Microsoft SQL Server.
                options.UseSqlServer(Configuration["ConnectionStrings:IdentityContext"]);

                // Register the entity sets needed by OpenIddict.
                // Note: use the generic overload if you need
                // to replace the default OpenIddict entities.
                options.UseOpenIddict();
            });

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            // Register the Identity services.
            services.AddIdentity<ApplicationUser, ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            // Configure Identity to use the same JWT claims as OpenIddict instead
            // of the legacy WS-Federation claims it uses by default (ClaimTypes),
            // which saves you from doing the mapping in your authorization controller.
            services.Configure<IdentityOptions>(options =>
            {
                options.ClaimsIdentity.UserNameClaimType = OpenIdConnectConstants.Claims.Name;
                options.ClaimsIdentity.UserIdClaimType = OpenIdConnectConstants.Claims.Subject;
                options.ClaimsIdentity.RoleClaimType = OpenIdConnectConstants.Claims.Role;


                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 1;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvxyz1234567890!@#$%^&*()_+<>:|";
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
                options.Lockout.MaxFailedAccessAttempts = 3;
            });

            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                })

                .AddJwtBearer(options =>
                {
                    options.Authority = "http://localhost:5000";
                    options.Audience = "resource-server";
                    options.RequireHttpsMetadata = false;
                });

           
                // Register the OpenIddict services.
                services.AddOpenIddict(options =>
            {
                // Register the Entity Framework stores.
                options.AddEntityFrameworkCoreStores<ApplicationDbContext>();
                // Register the ASP.NET Core MVC binder used by OpenIddict.
                // Note: if you don't call this method, you won't be able to
                // bind OpenIdConnectRequest or OpenIdConnectResponse parameters.
                options.AddMvcBinders();

                // Enable the authorization, logout, token and userinfo endpoints.
                options.EnableAuthorizationEndpoint("/connect/authorize")
                       .EnableLogoutEndpoint("/connect/logout")
                       .EnableTokenEndpoint("/connect/token")
                       .EnableUserinfoEndpoint("/api/userinfo");

                // Note: the Mvc.Client sample only uses the code flow and the password flow, but you
                // can enable the other flows if you need to support implicit or client credentials.
                options
                       .AllowPasswordFlow()
                       .AllowRefreshTokenFlow();

                options.UseJsonWebTokens();
                options.AddEphemeralSigningKey();
                // Make the "client_id" parameter mandatory when sending a token request.
                options.RequireClientIdentification();
                // During development, you can disable the HTTPS requirement.

                options.DisableHttpsRequirement();
            });
        }

        // ReSharper disable once UnusedMember.Local
        private static void AddAndConfigurePolicies(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                // we can make this more granular 
                //options.AddPolicy(AppPolicies.Somepolicy, policy => policy.RequireClaim(AppClaimTypes.SomeClaim));
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddSerilog();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }


            app.UseStaticFiles();

            app.UseStatusCodePagesWithReExecute("/error");

            app.UseAuthentication();

            app.UseExampleMiddleware();

            app.UseMvcWithDefaultRoute();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS etc.), specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = "apidocs";
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My Web API");
            });


            // Seed the database with the sample applications.
            // Note: in a real world application, this step should be part of a setup script.
            InitializeAsync(app.ApplicationServices, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            // Create a new service scope to ensure the database context is correctly disposed when this methods returns.
            using (var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.EnsureCreatedAsync(cancellationToken);

                var manager = scope.ServiceProvider.GetRequiredService<OpenIddictApplicationManager<OpenIddictApplication>>();

                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

                if(!roleManager.Roles.Any(x => x.Name == "Admin"))
                {
                    await roleManager.CreateAsync(new ApplicationRole
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "Admin",
                        NormalizedName = "admin"
                    });
                }

                if (!roleManager.Roles.Any(x => x.Name == "User"))
                {
                    await roleManager.CreateAsync(new ApplicationRole
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "User",
                        NormalizedName = "user"
                    });
                }

                if (await userManager.FindByEmailAsync("cioclea.doru@gmail.com") == null)
                {
                    // create the user 
                    var applicationUser = new ApplicationUser
                    {
                        Email = "cioclea.doru@gmail.com",
                        EmailConfirmed = true,
                        Id = Guid.NewGuid().ToString(),
                        UserName = "doruc"
                    };

                    var result = await userManager.CreateAsync(applicationUser, "secret");
                    if (!result.Succeeded)
                    {
                        StringBuilder builder = new StringBuilder();
                        foreach (var err in result.Errors)
                        {
                            builder.AppendLine(err.Description);
                        }

                        throw new Exception(builder.ToString());
                    }

                    await userManager.SetLockoutEnabledAsync(applicationUser, false);
                    await userManager.AddToRolesAsync(applicationUser, new[] {"Admin", "User"});
                }

                if (await manager.FindByClientIdAsync("mvc", cancellationToken) == null)
                {
                    var application = new OpenIddictApplication
                    {
                        ClientId = "mvc",
                        DisplayName = "MVC client application",
                        LogoutRedirectUri = "http://localhost:53507/signout-callback-oidc",
                        RedirectUri = "http://localhost:53507/signin-oidc"
                    };

                    await manager.CreateAsync(application, "901564A5-E7FE-42CB-B10D-61EF6A8F3654", cancellationToken);
                }

                // To test this sample with Postman, use the following settings:
                //
                // * Authorization URL: http://localhost:54540/connect/authorize
                // * Access token URL: http://localhost:54540/connect/token
                // * Client ID: postman
                // * Client secret: [blank] (not used with public clients)
                // * Scope: openid email profile roles
                // * Grant type: authorization code
                // * Request access token locally: yes
                if (await manager.FindByClientIdAsync("postman", cancellationToken) == null)
                {
                    var application = new OpenIddictApplication
                    {
                        ClientId = "postman",
                        DisplayName = "Postman",
                        RedirectUri = "https://www.getpostman.com/oauth2/callback"
                    };

                    await manager.CreateAsync(application, cancellationToken);
                }
            }
        }
    }
}
