using store.web.Email;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Email
var smtpSettings = builder.Configuration
    .GetSection(SmtpSettings.SectionName)
    .Get<SmtpSettings>() ?? new SmtpSettings();

builder.Services.AddSingleton(smtpSettings);
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
