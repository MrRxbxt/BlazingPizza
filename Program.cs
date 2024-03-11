using BlazingPizza.Data;
using BlazingPizza.Endpoints;
using BlazingPizza.BlazorServices;
using Microsoft.EntityFrameworkCore;
using BlazingPizza.APIServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddHttpClient();
//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

IConfiguration _configuration = builder.Configuration;
var connectionString = _configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<PizzaStoreContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddSingleton(serviceProvider =>
{
	var configuration = serviceProvider.GetRequiredService<IConfiguration>();
	var connectionString = configuration.GetConnectionString("DefaultConnection") ??
							throw new ApplicationException("The connection string is null");
	return new SqlConnectionFactory(connectionString);
});

builder.Services.AddScoped<OrderState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
}
else
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapOrdersEndpoints();

// Initialize the database
var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
using (var scope = scopeFactory.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<PizzaStoreContext>();
	if (db.Database.EnsureCreated())
	{
		SeedData.Initialize(db);
	}
}

//app.MapDefaultControllerRoute();
//app.MapControllers();

app.Run();