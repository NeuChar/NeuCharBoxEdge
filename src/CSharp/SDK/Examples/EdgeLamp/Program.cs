using EdgeLamp.Services;
using Senparc.Ncf.Database;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();


//1、添加（注册） NCF 服务（必须）
Senparc.Xncf.NeuCharBoxEdgeSimp.ProgramExtensions.AddNcf(builder, new EdgeLamp.Register());

//开发者自己的Service
builder.Services.AddSingleton<GpioService>();
builder.Services.AddSingleton<LampService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//2、Use NCF（必须）
Senparc.Xncf.NeuCharBoxEdgeSimp.ProgramExtensions.UseNcf<BySettingDatabaseConfiguration>(app);


// Configure the HTTP request pipeline.

app.UseAuthorization();

app.MapControllers();

//3、运行在5000端口
app.Run("http://0.0.0.0:5000");
