var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor(); // ITenantProvider で必要

// JWT認証の設定（省略せずに適切に設定してください）
// builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)...

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();