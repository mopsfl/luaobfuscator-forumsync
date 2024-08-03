using DSharpPlus;
using DSharpPlus.Entities;
using System.Text.RegularExpressions;
using dotenv.net;

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
            DiscordConfiguration config = new()
            {
                Token = token,
                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents
            }; discordClient = new(config);

            DiscordActivity status = new("Forum", ActivityType.Watching);
            await discordClient.ConnectAsync(status, UserStatus.Online);

            // listen to messageCreate to update messageCache
            discordClient.MessageCreated += (client, eventArgs) =>
            {
                ForumSync.AddNewMessage(eventArgs);
                return Task.CompletedTask;
            };

            discordClient.MessageDeleted += (client, eventArgs) =>
            {
                ForumSync.RemovedDeletedMessage(eventArgs);
                return Task.CompletedTask;
            };

            // everything here is just for the concept obv.
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.UseStaticFiles();
            app.MapGet("/forum/{channelId}", async (ulong channelId) =>
            {
                var data = await ForumSync.FetchForumData(channelId);
                if (data == null) return Results.NotFound("Channel not found.");
                DiscordChannel channel = await discordClient.GetChannelAsync(channelId);

                string htmlStuff = $"<a href='/forum/{channelId}' class='path'>{channel.Name}</a>";
                foreach (var thread in data)
                {
                    htmlStuff += $@"<a class='threaditem' href='/forum/{channelId}/{thread.Id}'>
                        <img src='{thread.AuthorAvatarUrl}' loading='lazy'>
                        <div>
                            <h3>{thread.Name}</h3>
                            <p><span>{thread.AuthorName}</span> <span>{thread.CreatedTimestampString}</span></p>
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
                    if (message.Id == thread.FirstMessage?.Id || Utils.ValidateMessage(message) == false) continue; // skip first message wich is the thread content yk

                    htmlStuff += $@"<div class='threaditem grid'>
                        <div class='threaditem-author'>
                            <img src='{message.Author.AvatarUrl}'>
                            <div>
                                <p><span>@{message.Author.Username}</span> <span>{message.CreationTimestamp:HH:mm - dd/MM/yyyy}</span></p>
                            </div>
                        </div>
                        <span>{Utils.FormatMessageContent(message)}</span>
                    </div>";
                }

                string finalHtml = htmlContent2.Replace("<!--content1-->", $@"<div style='display: flex; gap: 5px'><a href='/forum/{channelId}' class='path'>{channel.Name}</a> <a href='/forum/{channelId}/{thread.Id}' class='path'>{thread.Name}</a></div>
                <a class='threaditem' style='margin-bottom: 10px'>
                    <img src='{thread.AuthorAvatarUrl}' loading='lazy'>
                    <div class='threaditem-author grid'>
                        <h3>{thread.Name}</h3>
                        <p><span>@{thread.AuthorName}</span> <span>{thread.CreatedTimestampString}</span></p>
                    </div>
                    <div class='break'></div>
                    <span>{Utils.FormatMessageContent(thread.FirstMessage)}</span>
                </a>").Replace("<!--content2-->", htmlStuff).Replace("<!--guildIcon-->", channel.Guild.IconUrl);

                return Results.Content(finalHtml, "text/html");
            });

            app.Run();
            await Task.Delay(-1);
        }
    }
}