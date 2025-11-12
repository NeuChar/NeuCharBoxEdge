using EdgeLed;
using EdgeLed.Services;
using Microsoft.OpenApi.Models;
using Senparc.Ncf.Database;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.



builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//builder.Services.AddSignalR(); // 添加 SignalR 支持

//1、添加（注册） NCF 服务（必须）
Senparc.Xncf.NeuCharBoxEdgeSimp.ProgramExtensions.AddNcf(builder, new EdgeLed.Register());

//开发者自己的Service
builder.Services.AddSingleton<TM1637DisplayService>();



var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//2、Use NCF（必须）
Senparc.Xncf.NeuCharBoxEdgeSimp.ProgramExtensions.UseNcf<BySettingDatabaseConfiguration>(app);

app.UseAuthorization();

app.MapControllers();

//3、运行在5000端口
app.Run("http://0.0.0.0:5000");
