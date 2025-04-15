using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.WebSocket;
using System.Text.RegularExpressions;
using dotenv.net;
using DSharpPlus.Net;
using Newtonsoft.Json.Serialization;

namespace luaobfuscator_forumsync
{
    public sealed class Program
    {
        public static readonly Regex urlRegex = new(
            @"^(https?:\/\/)?(www\.)?((\d{1,3}\.){3}\d{1,3}(:\d{1,5})?|([\w\-]+\.)+[a-zA-Z]{2,9})(\/.*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline
        );
        public static DiscordClient? discordClient;
        private static readonly string htmlContent1 = @"<!DOCTYPE html>
        <html lang='en'>
        <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <title>Forum Sync Test</title>
            <link rel='stylesheet' href='/style.css'>
            <link rel='icon' type='image/x-icon' href='<!--guildIcon-->'>
        </head>
        <body>
        <div class='container'>
            <!--content-->
        </div>
        </body>
        </html>";
        private static readonly string htmlContent2 = @"<!DOCTYPE html>
        <html lang='en'>
        
        <head>
            <meta charset='UTF-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            <title>Forum Sync Test</title>
            <link rel='stylesheet' href='/style.css'>
            <link rel='icon' type='image/x-icon' href='<!--guildIcon-->'>
        </head>
        
        <body>
        <div class='container'>
            <!--content1-->
            <!--content2-->
        </div>
        </body>
        
        </html>";
        public static async Task Main()
        {
            DotEnv.Load();
            string? token = Environment.GetEnvironmentVariable("TOKEN");
            if (token == null) { Console.WriteLine("Token is null."); return; }

            DiscordClientBuilder clientBuilder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.All)
            .ConfigureEventHandlers(
                b => b.HandleMessageCreated(async (s, message) =>
                {
                    ForumSync.AddNewMessage(message);
                }).HandleThreadCreated(async (s, thread) =>
                {
                    ForumSync.threadCache.Clear(); // TODO: dont clear lol
                })
            ).SetLogLevel(LogLevel.Debug);
            discordClient = clientBuilder.Build();
            await clientBuilder.ConnectAsync();

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.UseStaticFiles();
            app.MapGet("/", async () =>
            {
                return Results.Ok();
            });
            app.MapGet("/forum/{channelId}", async (ulong channelId) =>
            {
                var data = await ForumSync.FetchForumData(channelId);
                if (data == null) return Results.NotFound("Channel not found.");
                if (discordClient == null) return Results.InternalServerError("internal error! discord client not set up.");
                DiscordChannel channel = await discordClient.GetChannelAsync(channelId);

                string htmlStuff = $"<a href='/forum/{channelId}' class='path'>{channel.Name}</a>";
                foreach (var thread in data)
                {
                    htmlStuff += $@"<a class='threaditem' href='/forum/{channelId}/{thread.Id}'>
                        <img src='{thread.AuthorAvatarUrl}' loading='lazy'>
                        <div>
                            <h3>{thread.Name}</h3>
                            <p><span class='inline-text'>{thread.AuthorName}</span> <span class='inline-text'>{thread.CreatedTimestampString}</span></p>
                        </div>
                    </a>";
                }

                string finalHtml = htmlContent1.Replace("<!--content-->", htmlStuff).Replace("<!--guildIcon-->", channel.Guild.IconUrl);
                return Results.Content(finalHtml, "text/html");
            });
            app.MapGet("/forum/{channelId}/{threadId}", async (ulong channelId, ulong threadId) =>
            {
                var data = await ForumSync.FetchForumData(channelId);
                if (data == null) return Results.NotFound("Channel not found.");
                DiscordChannel channel = await discordClient.GetChannelAsync(channelId);

                string htmlStuff = "";
                var thread = data.FirstOrDefault(t => t.Id == threadId);
                if (thread == null) return Results.NotFound("Thread not found.");

                foreach (var message in thread.Messages)
                {
                    if (message.Id == thread.FirstMessage?.Id || Utils.ValidateMessage(message) == false) continue;
                    string authorAvatarUrl = message.Author?.AvatarUrl ?? message.Author?.DefaultAvatarUrl ?? "";
                    string authorUsername = message.Author?.Username ?? "Deleted User";

                    htmlStuff += $@"<div class='threaditem grid' id='{message.Id}'>
                        <div class='threaditem-author'>
                            <img src='{authorAvatarUrl}'>
                            <div>
                                <p><span class='inline-text'>@{authorUsername}</span> <span class='inline-text'>{message.CreationTimestamp:HH:mm - dd/MM/yyyy}</span></p>
                            </div>
                        </div>
                        <span class='message-content''>{Utils.FormatMessageContent(message)}</span>
                    </div>";
                }

                string finalHtml = htmlContent2.Replace("<!--content1-->", $@"<div style='display: flex; gap: 5px'><a href='/forum/{channelId}' class='path'>{channel.Name}</a> <a href='/forum/{channelId}/{thread.Id}' class='path'>{thread.Name}</a></div>
                <a class='threaditem' style='margin-bottom: 10px'>
                    <img src='{thread.AuthorAvatarUrl}' loading='lazy'>
                    <div class='threaditem-author grid'>
                        <h3>{thread.Name}</h3>
                        <p><span class='inline-text'>@{thread.AuthorName}</span> <span class='inline-text'>{thread.CreatedTimestampString}</span></p>
                    </div>
                    <div class='break'></div>
                    <span class='message-content'>{Utils.FormatMessageContent(thread.FirstMessage)}</span>
                </a>").Replace("<!--content2-->", htmlStuff).Replace("<!--guildIcon-->", channel.Guild.IconUrl);

                return Results.Content(finalHtml, "text/html");
            });
            app.Run();

            await discordClient.ConnectAsync(new DiscordActivity("Forums", DiscordActivityType.Watching), DiscordUserStatus.Online);
            await Task.Delay(-1);
        }
    }
}