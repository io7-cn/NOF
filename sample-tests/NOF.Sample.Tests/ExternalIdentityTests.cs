using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NOF.Sample.Identity;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class ExternalIdentityTests
{
    [Theory]
    [InlineData(null, "/demo.html")]
    [InlineData("https://other.test/", "/demo.html")]
    [InlineData("//other.test/", "/demo.html")]
    [InlineData("/\\other.test/", "/demo.html")]
    [InlineData("/permissions-demo?tab=read", "/permissions-demo?tab=read")]
    public void LoginReturn_StaysWithinTheApplication(string? value, string expected)
        => Assert.Equal(expected, ExternalIdentity.LocalReturnUrl(value));

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public void NativeIdentityConsumer_ValidatesAuthorityAndAudience(string environment, bool httpsRequired)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["Authentication:Authority"] = "https://identity.example/realms/demo";
        builder.Configuration["Authentication:ClientSecret"] = "test-only-secret";
        builder.Services.AddSampleIdentity(builder.Configuration, builder.Environment);
        using var host = builder.Build();
        var oidc = host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        Assert.Equal("https://identity.example/realms/demo", oidc.Authority);
        Assert.True(oidc.UsePkce);
        Assert.False(oidc.MapInboundClaims);
        Assert.Equal(httpsRequired, oidc.RequireHttpsMetadata);
        var bearer = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(oidc.Authority, bearer.Authority);
        Assert.Equal("nof-sample", bearer.Audience);
        Assert.Equal(httpsRequired, bearer.RequireHttpsMetadata);
        var cookies = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal("nof.sample.session", cookies.Cookie.Name);
    }
}
