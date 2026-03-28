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
using System.Threading;
using System.Globalization;
using Newtonsoft.Json;

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
            var newGameIdsInLibrary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newLibraryGames = PlayniteApi.Database.Games.ToList()
                .Where(g => g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId)
                .ToList();

            foreach (var game in newLibraryGames)
            {
                if (!string.IsNullOrEmpty(game.GameId))
                {
                    newGameIdsInLibrary.Add(game.GameId);
                }
            }

            LibraryGames = newLibraryGames;
            GameIdsInLibrary = newGameIdsInLibrary;
        }

        private string GetNormalizedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var lower = name.ToLowerInvariant();
            lower = Regex.Replace(lower, @"[^a-z0-9]", "");
            return lower;
        }

        public Game GetLibraryGameFromGamePassGame(GamePassGame gamePassGame)
        {
            return LibraryGames?.FirstOrDefault(g => (g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId) &&
                g.GameId.Equals(gamePassGame.GameId));
        }

        public Game GetLibraryGameFromGamePassGameAnySource(GamePassGame gamePassGame)
        {
            // Use the snapshot if available, otherwise hit the database but with ToList() to avoid concurrent modification issues
            if (LibraryGames != null)
            {
                return LibraryGames.FirstOrDefault(g => (g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId) &&
                    g.GameId.Equals(gamePassGame.GameId, StringComparison.OrdinalIgnoreCase));
            }

            return PlayniteApi.Database.Games.ToList()
                .FirstOrDefault(g => (g.PluginId == pluginId || g.PluginId == xboxLibraryPluginId) &&
                g.GameId.Equals(gamePassGame.GameId, StringComparison.OrdinalIgnoreCase));
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

        public int AddGamePassListToLibrary(List<GamePassGame> gamePassGamesList, GlobalProgressActionArgs progressArgs = null)
        {
            var addedCount = 0;
            if (progressArgs != null)
            {
                progressArgs.CurrentProgressValue = 0;
                progressArgs.ProgressMaxValue = gamePassGamesList.Count;
                progressArgs.Text = "Scanning Game Pass catalog...";
            }

            // Local lists for batch commitment
            var newGamesToAdd = new List<Tuple<Game, GamePassGame>>();
            var updatesToCommit = new List<Tuple<Game, List<Guid>, List<Guid>>>(); // Game, List of Tags, List of Platforms
            var currentSyncAddedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Pre-resolve all developers and publishers
            var allCompanyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var game in gamePassGamesList)
            {
                if (game.Developers != null) foreach (var dev in game.Developers) if (!string.IsNullOrEmpty(dev)) allCompanyNames.Add(dev);
                if (game.Publishers != null) foreach (var pub in game.Publishers) if (!string.IsNullOrEmpty(pub)) allCompanyNames.Add(pub);
            }

            PlayniteApi.MainView.UIDispatcher.Invoke(new Action(() =>
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var companyName in allCompanyNames)
                    {
                        PlayniteApi.Database.Companies.Add(companyName);
                    }
                }
            }));

            // 2. Scan and Prepare Changes (Background Thread)
            var libraryNormalizedNames = new Dictionary<string, Game>();
            if (LibraryGames != null)
            {
                foreach (var libGame in LibraryGames)
                {
                    var norm = GetNormalizedName(libGame.Name);
                    if (!string.IsNullOrEmpty(norm) && !libraryNormalizedNames.ContainsKey(norm))
                    {
                        libraryNormalizedNames[norm] = libGame;
                    }
                }
            }

            foreach (GamePassGame game in gamePassGamesList)
            {
                try
                {
                    if (progressArgs?.CancelToken.IsCancellationRequested == true) break;
                    if (progressArgs != null) progressArgs.CurrentProgressValue++;

                    if (!syncConsoleGames && game.IsConsole && !game.IsPC) continue;
                    if (GameIdsInLibrary != null && GameIdsInLibrary.Contains(game.GameId)) continue;
                    if (currentSyncAddedIds.Contains(game.GameId)) continue;

                    var existingGame = LibraryGames?.FirstOrDefault(g => g.GameId.Equals(game.GameId, StringComparison.OrdinalIgnoreCase));
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
                        if (progressArgs != null) progressArgs.Text = "Scanning catalog and identifying updates...";
                        
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

                        if (game.ReleaseDate.Year != 2799)
                        {
                            newGame.ReleaseDate = new ReleaseDate(game.ReleaseDate);
                        }

                        newGamesToAdd.Add(new Tuple<Game, GamePassGame>(newGame, game));
                        currentSyncAddedIds.Add(game.GameId);
                    }
                    else if (existingGame.PluginId == pluginId)
                    {
                        var targetPlatforms = new List<Guid>();
                        if (game.IsPC) targetPlatforms.AddRange(platformsList);
                        if (game.IsConsole) targetPlatforms.AddRange(consolePlatformsList);

                        var targetTags = new List<Guid>();
                        if (game.IsPC) targetTags.Add(gameAddedTag.Id);
                        if (game.IsConsole) targetTags.Add(gameAddedConsoleTag.Id);

                        updatesToCommit.Add(new Tuple<Game, List<Guid>, List<Guid>>(existingGame, targetTags, targetPlatforms));
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error preparing {game.Name} for library update");
                }
            }

            // 3. One-Shot Commit (UI Thread)
            if (progressArgs != null)
            {
                progressArgs.Text = "Adding games to library (this may take a moment)...";
                // Give UI thread a moment to catch the message change before we block it
                Thread.Sleep(100);
            }

            PlayniteApi.MainView.UIDispatcher.Invoke(new Action(() =>
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var entry in newGamesToAdd)
                    {
                        var newGame = entry.Item1;
                        var source = entry.Item2;
                        
                        PlayniteApi.Database.Games.Add(newGame);

                        // Handle files while we have the ID
                        if (FileSystem.FileExists(source.CoverImage))
                        {
                            newGame.CoverImage = PlayniteApi.Database.AddFile(source.CoverImage, newGame.Id);
                        }
                        if (FileSystem.FileExists(source.Icon))
                        {
                            newGame.Icon = PlayniteApi.Database.AddFile(source.Icon, newGame.Id);
                        }
                        if (FileSystem.FileExists(source.BackgroundImage))
                        {
                            newGame.BackgroundImage = PlayniteApi.Database.AddFile(source.BackgroundImage, newGame.Id);
                        }
                        else if (!source.BackgroundImageUrl.IsNullOrEmpty())
                        {
                            var fileName = string.Format("{0}_background.jpg", source.ProductId);
                            var downloadPath = Path.Combine(PlayniteApi.Database.GetFileStoragePath(newGame.Id), fileName);
                            if (FileSystem.FileExists(downloadPath))
                            {
                                newGame.BackgroundImage = string.Format("{0}/{1}", newGame.Id.ToString(), fileName);
                            }
                        }

                        PlayniteApi.Database.Games.Update(newGame);
                        addedCount++;
                    }

                    foreach (var update in updatesToCommit)
                    {
                        var game = update.Item1;
                        var tags = update.Item2;
                        var plts = update.Item3;
                        var changed = false;

                        foreach (var tagId in tags)
                        {
                            var tag = PlayniteApi.Database.Tags.Get(tagId);
                            if (tag != null && PlayniteUtilities.AddTagToGame(PlayniteApi, game, tag)) changed = true;
                        }

                        var currentPlatforms = game.PlatformIds != null ? new List<Guid>(game.PlatformIds) : new List<Guid>();
                        foreach (var platId in plts)
                        {
                            if (!currentPlatforms.Contains(platId))
                            {
                                currentPlatforms.Add(platId);
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            game.PlatformIds = currentPlatforms;
                            PlayniteApi.Database.Games.Update(game);
                        }
                    }
                }
            }));

            PlayniteApi.MainView.UIDispatcher.Invoke(new Action(() =>
            {
                RefreshLibraryItems();
            }));
            
            return addedCount;
        }

        private List<Guid> arrayToCompanyGuids(List<string> array)
        {
            var companyGuids = new List<Guid>();
            if (array == null)
            {
                return companyGuids;
            }

            foreach (var str in array)
            {
                var company = PlayniteApi.Database.Companies.Add(str);
                companyGuids.Add(company.Id);
            }

            return companyGuids;
        }

        private string StringToHtml(string str, bool replaceLineEnds)
        {
            if (string.IsNullOrEmpty(str))
            {
                return string.Empty;
            }

            if (replaceLineEnds)
            {
                return str.Replace("\r\n", "<br>").Replace("\n", "<br>");
            }

            return str;
        }

        public bool AddGameToLibrary(GamePassGame game, bool showGameAddDialog, Game existingGameCheck = null)
        {
            if (game == null || string.IsNullOrEmpty(game.GameId)) return false;
            if (game.ProductType != ProductType.Game && game.ProductType != ProductType.EaGame) return false;

            var existingGame = existingGameCheck ?? GetLibraryGameFromGamePassGameAnySource(game);
            if (existingGame != null) return false;

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

            if (game.ReleaseDate.Year != 2799) newGame.ReleaseDate = new ReleaseDate(game.ReleaseDate);

            PlayniteApi.MainView.UIDispatcher.Invoke(new Action(() =>
            {
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    PlayniteApi.Database.Games.Add(newGame);

                    if (FileSystem.FileExists(game.CoverImage)) newGame.CoverImage = PlayniteApi.Database.AddFile(game.CoverImage, newGame.Id);
                    if (FileSystem.FileExists(game.Icon)) newGame.Icon = PlayniteApi.Database.AddFile(game.Icon, newGame.Id);
                    if (FileSystem.FileExists(game.BackgroundImage)) newGame.BackgroundImage = PlayniteApi.Database.AddFile(game.BackgroundImage, newGame.Id);

                    PlayniteApi.Database.Games.Update(newGame);
                }
                
                RefreshLibraryItems();
            }));

            if (showGameAddDialog)
            {
                PlayniteApi.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString("LOCGamePass_Catalog_Browser_AddGameResultsMessage"), game.Name));
            }

            return true;
        }
    }
}
