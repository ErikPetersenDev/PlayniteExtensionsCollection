using GamePassCatalogBrowser.Models;
using Playnite.SDK;
using Playnite.SDK.Models;
using PlayniteUtilitiesCommon;
using PluginsCommon;
using FlowHttp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;

namespace GamePassCatalogBrowser
{
    class XboxLibraryHelper
    {
        private IPlayniteAPI PlayniteApi;
        private ILogger logger = LogManager.GetLogger();
        private readonly Guid pluginId = Guid.Parse("7e4fbb5e-2ae3-48d4-8ba0-6b30e7a4e287");
        private readonly Guid xboxLibraryPluginId = Guid.Parse("0417a80d-6e46-4dc2-9fa0-c0b70a322c3f");
        private readonly List<Guid> platformsList;
        private readonly List<Guid> consolePlatformsList;
        private readonly Guid sourceId;
        private readonly Tag gameAddedTag;
        private readonly Tag gameRemovedTag;
        private readonly Tag gameAddedConsoleTag;
        private readonly Tag gameRemovedConsoleTag;
        private bool syncConsoleGames = false;
        public IEnumerable<Game> LibraryGames;
        public HashSet<string> GameIdsInLibrary;

        public XboxLibraryHelper(IPlayniteAPI api, bool _syncConsoleGames = false)
        {
            PlayniteApi = api;
            syncConsoleGames = _syncConsoleGames;
            RefreshLibraryItems();

            var pcPlatform = PlayniteApi.Database.Platforms.Add("PC (Windows)");
            platformsList = new List<Guid> { pcPlatform.Id };

            var xboxOnePlatform = PlayniteApi.Database.Platforms.Add("Xbox One");
            var xboxSeriesPlatform = PlayniteApi.Database.Platforms.Add("Xbox Series X|S");
            consolePlatformsList = new List<Guid> { xboxOnePlatform.Id, xboxSeriesPlatform.Id };

            var sourceXboxGamePass = PlayniteApi.Database.Sources.Add("Xbox Game Pass");
            sourceId = sourceXboxGamePass.Id;

            gameAddedTag = PlayniteApi.Database.Tags.Add("Game Pass (PC)");
            gameRemovedTag = PlayniteApi.Database.Tags.Add("Game Pass (Formerly on) (PC)");
            gameAddedConsoleTag = PlayniteApi.Database.Tags.Add("Game Pass (Console)");
            gameRemovedConsoleTag = PlayniteApi.Database.Tags.Add("Game Pass (Console) (Formerly on)");
        }

        public void RefreshLibraryItems()
        {
            LibraryGames = PlayniteApi.Database.Games.
                Where(g => g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId);

            var gamesOnLibrary = new HashSet<string>();
            foreach (Game game in LibraryGames)
            {
                gamesOnLibrary.Add(game.GameId);
            }

            GameIdsInLibrary = gamesOnLibrary;
        }

        private List<Guid> arrayToCompanyGuids(List<string> array)
        {
            var list = new List<Guid>();
            foreach (var str in array)
            {
                var company = PlayniteApi.Database.Companies.Add(str);
                list.Add(company.Id);
            }

            return list;
        }

        public static string GetNormalizedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var normalized = name.ToLowerInvariant();
            normalized = normalized.Replace("™", "").Replace("®", "").Replace("©", "");
            normalized = Regex.Replace(normalized, @"\s+", " "); // Replace multiple spaces with single space
            return normalized.Trim();
        }

        private static string StringToHtml(string s, bool nofollow)
        {
            s = WebUtility.HtmlEncode(s);
            string[] paragraphs = s.Split(new string[] { "\r\n\r\n" }, StringSplitOptions.None);
            StringBuilder sb = new StringBuilder();
            foreach (string par in paragraphs)
            {
                sb.AppendLine("<p>");
                string p = par.Replace(Environment.NewLine, "<br />\r\n");
                if (nofollow)
                {
                    p = Regex.Replace(p, @"\[\[(.+)\]\[(.+)\]\]", "<a href=\"$2\" rel=\"nofollow\">$1</a>");
                    p = Regex.Replace(p, @"\[\[(.+)\]\]", "<a href=\"$1\" rel=\"nofollow\">$1</a>");
                }
                else
                {
                    p = Regex.Replace(p, @"\[\[(.+)\]\[(.+)\]\]", "<a href=\"$2\">$1</a>");
                    p = Regex.Replace(p, @"\[\[(.+)\]\]", "<a href=\"$1\">$1</a>");
                }
                sb.AppendLine(p);
                sb.AppendLine("</p>");
            }
            return sb.ToString();
        }

        public Game GetLibraryGameFromGamePassGame(GamePassGame gamePassGame)
        {
            return PlayniteApi.Database.Games
                .FirstOrDefault(g => g.PluginId.Equals(pluginId) &&
                g.GameId.Equals(gamePassGame.GameId) &&
                g.SourceId != null &&
                g.SourceId.Equals(sourceId));
        }

        public Game GetLibraryGameFromGamePassGameAnySource(GamePassGame gamePassGame)
        {
            return PlayniteApi.Database.Games
                .FirstOrDefault(g => (g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId) &&
                g.GameId.Equals(gamePassGame.GameId));
        }

        public bool RemoveGamePassGame(GamePassGame gamePassGame)
        {
            var game = GetLibraryGameFromGamePassGame(gamePassGame);
            if (game == null)
            {
                return false;
            }

            if (game.Playtime > 0)
            {
                game.SourceId = PlayniteApi.Database.Sources.Add("Xbox").Id;
                PlayniteApi.Database.Games.Update(game);
                return false;
            }
            else
            {
                PlayniteApi.Database.Games.Remove(game.Id);
                GameIdsInLibrary.Remove(game.GameId);
                return true;
            }
        }

        public void AddExpiredTag(GamePassGame gamePassGame)
        {
            var gameInLibrary = GetLibraryGameFromGamePassGameAnySource(gamePassGame);
            if (gameInLibrary != null)
            {
                var gameRemoved = false;
                if (PlayniteUtilities.RemoveTagFromGame(PlayniteApi, gameInLibrary, gameAddedTag)) gameRemoved = true;
                if (PlayniteUtilities.RemoveTagFromGame(PlayniteApi, gameInLibrary, gameAddedConsoleTag)) gameRemoved = true;

                var tagAdded = false;
                if (gamePassGame.IsPC && PlayniteUtilities.AddTagToGame(PlayniteApi, gameInLibrary, gameRemovedTag)) tagAdded = true;
                if (gamePassGame.IsConsole && PlayniteUtilities.AddTagToGame(PlayniteApi, gameInLibrary, gameRemovedConsoleTag)) tagAdded = true;

                // Fallback for cleanly importing older cached tasks where IsPC isn't stored
                if (!tagAdded && PlayniteUtilities.AddTagToGame(PlayniteApi, gameInLibrary, gameRemovedTag)) tagAdded = true;

                gameInLibrary.SourceId = PlayniteApi.Database.Sources.Add("Xbox").Id;
                PlayniteApi.Database.Games.Update(gameInLibrary);
            }
        }

        public int AddGamePassListToLibrary (List<GamePassGame> gamePassGamesList, GlobalProgressActionArgs progressArgs = null)
        {
            var i = 0;
            if (progressArgs != null)
            {
                progressArgs.CurrentProgressValue = 0;
                progressArgs.ProgressMaxValue = gamePassGamesList.Count;
                progressArgs.Text = "Processing Game Pass catalog updates...";
            }

            // Create a fast-lookup dictionary for normalized names to avoid repeated expensive regex/replace calls
            var libraryNormalizedNames = new Dictionary<string, Game>();
            foreach (var libGame in LibraryGames)
            {
                var norm = GetNormalizedName(libGame.Name);
                if (!string.IsNullOrEmpty(norm) && !libraryNormalizedNames.ContainsKey(norm))
                {
                    libraryNormalizedNames[norm] = libGame;
                }
            }

            using (PlayniteApi.Database.BufferedUpdate())
            foreach (GamePassGame game in gamePassGamesList.ToList())
            {
                if (progressArgs?.CancelToken.IsCancellationRequested == true) break;
                if (progressArgs != null)
                {
                    progressArgs.CurrentProgressValue++;
                }
                if (!syncConsoleGames && game.IsConsole && !game.IsPC)
                {
                    continue;
                }

                var existingGame = LibraryGames.FirstOrDefault(g => g.GameId.Equals(game.GameId));
                if (existingGame == null)
                {
                    var normalizedCatalogName = GetNormalizedName(game.Name);
                    if (!string.IsNullOrEmpty(normalizedCatalogName) && libraryNormalizedNames.TryGetValue(normalizedCatalogName, out var match))
                    {
                        existingGame = match;
                    }
                }

                if (existingGame == null)
                {
                    if (progressArgs != null) progressArgs.Text = $"Adding: {game.Name}";
                    var success = AddGameToLibrary(game, false);
                    if (success == true)
                    {
                        i++;
                    }
                }
                else if (existingGame.PluginId == pluginId)
                {
                    var resultSaved = false;
                    if (game.IsPC && PlayniteUtilities.AddTagToGame(PlayniteApi, existingGame, gameAddedTag)) resultSaved = true;
                    if (game.IsConsole && PlayniteUtilities.AddTagToGame(PlayniteApi, existingGame, gameAddedConsoleTag)) resultSaved = true;

                    // Let's also enforce platforms if they were already in the library but lacked console platforms
                    if (game.IsConsole)
                    {
                        foreach (var platformId in consolePlatformsList)
                        {
                            if (!existingGame.PlatformIds.Contains(platformId))
                            {
                                existingGame.PlatformIds.Add(platformId);
                                resultSaved = true;
                            }
                        }
                        if (PlayniteUtilities.AddFeatureToGame(PlayniteApi, existingGame, "Xbox Series X|S")) resultSaved = true;
                    }

                    if (game.IsPC)
                    {
                        foreach (var platformId in platformsList)
                        {
                            if (!existingGame.PlatformIds.Contains(platformId))
                            {
                                existingGame.PlatformIds.Add(platformId);
                                resultSaved = true;
                            }
                        }
                    }

                    if (resultSaved)
                    {
                        PlayniteApi.Database.Games.Update(existingGame);
                    }
                }
                else
                {
                    // Match found in official Xbox Library - skip silently as requested
                }
            }

            RefreshLibraryItems();

            return i;
        }

        public bool AddGameToLibrary(GamePassGame game, bool showGameAddDialog)
        {
            if (game == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(game.GameId))
            {
                return false;
            }

            if (game.ProductType != ProductType.Game && game.ProductType != ProductType.EaGame)
            {
                return false;
            }

            var existingGame = GetLibraryGameFromGamePassGameAnySource(game);
            if (existingGame != null)
            {
                return false;
            }

            var newTags = new List<Guid>();
            if (game.IsPC) newTags.Add(gameAddedTag.Id);
            if (game.IsConsole) newTags.Add(gameAddedConsoleTag.Id);

            var newPlatforms = new List<Guid>();
            if (game.IsPC) newPlatforms.AddRange(platformsList);
            if (game.IsConsole) newPlatforms.AddRange(consolePlatformsList);

            var newGame = new Game
            {
                Name = game.Name,
                GameId = game.GameId,
                DeveloperIds = arrayToCompanyGuids(game.Developers),
                PublisherIds = arrayToCompanyGuids(game.Publishers),
                TagIds = newTags,
                PluginId = pluginId,
                PlatformIds = newPlatforms,
                Description = StringToHtml(game.Description, true),
                SourceId = sourceId,
                CompletionStatusId = PlayniteApi.ApplicationSettings.CompletionStatus.DefaultStatus
            };

            // #71 Certain games have incorrect dates in their release date,
            // having in common the year 2799
            if (game.ReleaseDate.Year != 2799)
            {
                newGame.ReleaseDate = new ReleaseDate(game.ReleaseDate);
            }

            PlayniteApi.Database.Games.Add(newGame);
            if (FileSystem.FileExists(game.CoverImage))
            {
                newGame.CoverImage = PlayniteApi.Database.AddFile(game.CoverImage, newGame.Id);
            }

            if (FileSystem.FileExists(game.Icon))
            {
                newGame.Icon = PlayniteApi.Database.AddFile(game.Icon, newGame.Id);
            }

            if (!game.BackgroundImageUrl.IsNullOrEmpty())
            {
                var fileName = string.Format("{0}.jpg", Guid.NewGuid().ToString());
                var downloadPath = Path.Combine(PlayniteApi.Database.GetFileStoragePath(newGame.Id), fileName);
                HttpRequestFactory.GetHttpFileRequest()
                    .WithUrl($"{game.BackgroundImageUrl}?mode=scale&q=90&h=1080&w=1920")
                    .WithDownloadTo(downloadPath)
                    .DownloadFile();
                if (FileSystem.FileExists(downloadPath))
                {
                    newGame.BackgroundImage = string.Format("{0}/{1}", newGame.Id.ToString(), fileName);
                }
            }

            PlayniteApi.Database.Games.Update(newGame);
            GameIdsInLibrary.Add(game.GameId);

            if (showGameAddDialog)
            {
                PlayniteApi.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString("LOCGamePass_Catalog_Browser_AddGameResultsMessage"), game.Name));
            }

            return true;
        }
    }
}
