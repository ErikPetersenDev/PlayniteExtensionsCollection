using Newtonsoft.Json;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GamePassCatalogBrowser.Models;
using System.Text.RegularExpressions;
using FlowHttp;
using PluginsCommon;

namespace GamePassCatalogBrowser.Services
{
    class GamePassCatalogBrowserService
    {
        private IPlayniteAPI playniteApi;
        private ILogger logger = LogManager.GetLogger();
        public List<GamePassGame> gamePassGamesList = new List<GamePassGame>();
        private readonly string userDataPath = string.Empty;
        private readonly string cachePath = string.Empty;
        private readonly string imageCachePath = string.Empty;
        private readonly string gameDataCachePath = string.Empty;
        public const string gamepassCatalogApiBaseUrl = @"https://catalog.gamepass.com/sigls/v2?id=fdd9e2a7-0fee-49f6-ad69-4354098401ff&language={0}&market={1}";
        public const string gamepassEaCatalogApiBaseUrl = @"https://catalog.gamepass.com/sigls/v2?id=1d33fbb9-b895-4732-a8ca-a55c8b99fa2c&language={0}&market={1}";
        public const string gamepassConsoleCatalogApiBaseUrl = @"https://catalog.gamepass.com/sigls/v2?id=f6f1f99f-9b49-4ccd-b3bf-4d9767a77f5e&language={0}&market={1}";
        public const string gamepassEaConsoleCatalogApiBaseUrl = @"https://catalog.gamepass.com/sigls/v2?id=b8900d09-a491-44cc-916e-32b5acae621b&language={0}&market={1}";
        public const string catalogDataApiBaseUrl = @"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={0}&market={1}&languages={2}&MS-CV=F.1";
        private readonly string gamepassCatalogApiUrl = string.Empty;
        private readonly string gamepassEaCatalogApiUrl = string.Empty;
        private readonly string gamepassConsoleCatalogApiUrl = string.Empty;
        private readonly string gamepassEaConsoleCatalogApiUrl = string.Empty;
        private readonly string languageCode = string.Empty;
        private readonly string countryCode = string.Empty;
        private readonly bool notifyCatalogUpdates;
        private readonly bool addExpiredTagToGames;
        private readonly bool addNewGames;
        private readonly bool removeExpiredGames;
        private readonly bool enableConsoleCatalog;
        private readonly bool showConsoleGamesInBrowser;
        public XboxLibraryHelper xboxLibraryHelper;

        public void DeleteCache()
        {
            FileSystem.ClearDirectory(cachePath);
            FileSystem.ClearDirectory(imageCachePath);
        }

        public GamePassCatalogBrowserService(IPlayniteAPI api, string dataPath, bool _notifyCatalogUpdates, bool _addExpiredTagToGames, bool _addNewGames, bool _removeExpiredGames, string _countryCode, bool _enableConsoleCatalog = false, bool _syncConsoleGames = false, bool _showConsoleGamesInBrowser = true, string _languageCode = "en-us")
        {
            playniteApi = api;
            userDataPath = dataPath;
            notifyCatalogUpdates = _notifyCatalogUpdates;
            addExpiredTagToGames = _addExpiredTagToGames;
            addNewGames = _addNewGames;
            removeExpiredGames = _removeExpiredGames;
            enableConsoleCatalog = _enableConsoleCatalog;
            showConsoleGamesInBrowser = _showConsoleGamesInBrowser;

            cachePath = Path.Combine(userDataPath, "cache");
            imageCachePath = Path.Combine(cachePath, "images");
            gameDataCachePath = Path.Combine(cachePath, "gamesCache.json");
            languageCode = _languageCode;
            countryCode = _countryCode;
            gamepassCatalogApiUrl = string.Format(gamepassCatalogApiBaseUrl, languageCode, countryCode);
            gamepassEaCatalogApiUrl = string.Format(gamepassEaCatalogApiBaseUrl, languageCode, countryCode);
            gamepassConsoleCatalogApiUrl = string.Format(gamepassConsoleCatalogApiBaseUrl, languageCode, countryCode);
            gamepassEaConsoleCatalogApiUrl = string.Format(gamepassEaConsoleCatalogApiBaseUrl, languageCode, countryCode);

            xboxLibraryHelper = new XboxLibraryHelper(api, _syncConsoleGames);

            if (!FileSystem.DirectoryExists(imageCachePath))
            {
                FileSystem.CreateDirectory(imageCachePath);
            }
        }

        public List<GamePassCatalogProduct> GetGamepassCatalog(string catalogUrl)
        {
            var gamePassGames = new List<GamePassCatalogProduct>();
            try
            {
                var downloadResult = HttpRequestFactory.GetHttpRequest().WithUrl(catalogUrl).DownloadString();
                if (!downloadResult.IsSuccess)
                {
                    return null;
                }
                
                var gamePassCatalog = JsonConvert.DeserializeObject<List<GamePassCatalogProduct>>(downloadResult.Content);
                foreach (var gamePassProduct in gamePassCatalog)
                {
                    if (gamePassProduct.Id == null)
                    {
                        continue;
                    }

                    gamePassGames.Add(gamePassProduct);
                }

                return gamePassGames;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error in ApiRequest {gamepassCatalogApiUrl}");
                return null;
            }
        }

        private static string RegexRemoveOnEnd(string input, string search)
        {
            string result = Regex.Replace(
                input,
                string.Format("{0}$", Regex.Escape(search)),
                "",
                RegexOptions.IgnoreCase
            );
            return result;
        }

        private string NormalizeGameName(string str)
        {
            if (str.IsNullOrEmpty())
            {
                return str;
            }

            str = str.Replace("(PC)", "").
                Replace("(Windows)", "").
                Replace("(Windows 10)", "").
                Replace("for Windows 10", "").
                Replace("- Windows 10", "").
                Replace("Windows 10", "").
                Replace(@"®", "").
                Replace(@"™", "").
                Replace(@"©", "").
                Trim();

            string[] linesToRemove =
            {
                ": Windows Edition",
                " - Windows Edition",
                " Windows Edition",
                " Windows 10",
                "- PC",
                " PC",
                " Windows",
                " Win10",
                " Win 10"
            };

            foreach (var lineToRemove in linesToRemove)
            {
                str = RegexRemoveOnEnd(str, lineToRemove);
            }

            return str.Trim();
        }


        private List<string> CompaniesStringToList(string companiesString)
        {
            var companiesList = new List<string>();

            // Replace ", Inc" for ". Inc" and other terms so it doesn't conflict in the split operation
            companiesString = companiesString.
                Replace("Developed by ", "").
                Replace(", Inc", ". Inc").
                Replace(", INC", ". INC").
                Replace(", inc", ". inc").
                Replace(", Llc", ". Llc").
                Replace(", LLC", ". LLC").
                Replace(", Ltd", ". Ltd").
                Replace(", LTD", ". LTD");

            var stringSeparators = new string[] { ", ", "|", "/", "+", " and ", " & " };
            var splitArray = companiesString.Split(stringSeparators, StringSplitOptions.None);
            foreach (var splittedString in splitArray)
            {
                companiesList.Add(splittedString.
                    Replace(". Inc", ", Inc").
                    Replace(". INC", ", INC").
                    Replace(". inc", ", inc").
                    Replace(". Llc", ", Llc").
                    Replace(". LLC", ", LLC").
                    Replace(". Ltd", ", Ltd").
                    Replace(". LTD", ", LTD").
                    Trim());
            }

            return companiesList;
        }

        private void AddGamesFromCatalogData(CatalogData catalogData, bool addChildProducts, ProductType gameProductType, bool isPC, bool isConsole, bool isChildProduct, string parentProductId, GlobalProgressActionArgs progressArgs = null)
        {
            if (progressArgs != null)
            {
                progressArgs.ProgressMaxValue += catalogData.Products.Length;
            }

            foreach (CatalogProduct product in catalogData.Products)
            {
                if (progressArgs?.CancelToken.IsCancellationRequested == true) return;

                if (product.ProductBSchema == "ProductAddOn;3")
                {
                    if (progressArgs != null) progressArgs.CurrentProgressValue++;
                    continue;
                }

                var existingGame = gamePassGamesList.FirstOrDefault(g => g.ProductId.Equals(product.ProductId));
                if (existingGame != null)
                {
                    if (isPC) existingGame.IsPC = true;
                    if (isConsole) existingGame.IsConsole = true;
                    existingGame.TempIsFound = true;
                    if (progressArgs != null) progressArgs.CurrentProgressValue++;
                    continue;
                }



                var childSubproductsList = new List<string>();
                if (product.Properties.PackageFamilyName.IsNullOrEmpty())
                {
                    if (addChildProducts)
                    {
                        AddGamePassProductsFromPackage(product, gameProductType, isPC, isConsole, childSubproductsList, progressArgs);
                    }
                    else
                    {
                        if (progressArgs != null) progressArgs.CurrentProgressValue++;
                        continue;
                    }
                }

                if (childSubproductsList.Count > 0)
                {
                    if (progressArgs != null) progressArgs.CurrentProgressValue++;
                    continue;
                }

                AddGamePassGameFromProduct(gameProductType, isPC, isConsole, isChildProduct, parentProductId, product, childSubproductsList, progressArgs);
                if (progressArgs != null) progressArgs.CurrentProgressValue++;
            }
        }

        private void AddGamePassProductsFromPackage(CatalogProduct product, ProductType gameProductType, bool isPC, bool isConsole, List<string> childSubproductsList, GlobalProgressActionArgs progressArgs = null)
        {
            var marketProperties = product.MarketProperties.FirstOrDefault();
            if (marketProperties == null)
            {
                return;
            }

            var idsForDataRequest = new List<string>();
            foreach (RelatedProduct relatedProduct in marketProperties.RelatedProducts)
            {
                if (relatedProduct.RelationshipType == "Bundle" || relatedProduct.RelationshipType == "Parent")
                {
                    childSubproductsList.Add(relatedProduct.RelatedProductId);
                    if (!gamePassGamesList.Any(g => g.ProductId.Equals(relatedProduct.RelatedProductId)))
                    {
                        idsForDataRequest.Add(relatedProduct.RelatedProductId);
                    }
                }
            }

            if (idsForDataRequest.Count == 0)
            {
                return;
            }

            var bigIdsParam = string.Join(",", idsForDataRequest);
            var catalogDataApiUrl = string.Format(catalogDataApiBaseUrl, bigIdsParam, countryCode, languageCode);
            try
            {
                var downloadResult = HttpRequestFactory.GetHttpRequest().WithUrl(catalogDataApiUrl).DownloadString();
                if (progressArgs?.CancelToken.IsCancellationRequested == true) return;
                
                if (downloadResult.IsSuccess)
                {
                    AddGamesFromCatalogData(JsonConvert.DeserializeObject<CatalogData>(downloadResult.Content), false, gameProductType, isPC, isConsole, true, product.ProductId, progressArgs);
                }
                else
                {
                    logger.Info($"Request {catalogDataApiUrl} not completed");
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error in ApiRequest {catalogDataApiUrl}");
            }
        }

        private void AddGamePassGameFromProduct(ProductType gameProductType, bool isPC, bool isConsole, bool isChildProduct, string parentProductId, CatalogProduct product, List<string> childSubproductsList, GlobalProgressActionArgs progressArgs = null)
        {
            if (progressArgs != null)
            {
                var title = product.LocalizedProperties[0].ProductTitle.TrimEnd('.', ' ', '\u2026');
                if (title.Length > 35)
                {
                    title = title.Substring(0, 35).TrimEnd('.', ' ') + "...";
                }
                progressArgs.Text = $"Adding: {title}";
            }
            var gamePassGame = GetGamePassGameFromProduct(gameProductType, isPC, isConsole, isChildProduct, parentProductId, product, childSubproductsList);
            gamePassGame.TempIsFound = true;
            gamePassGamesList.Add(gamePassGame);
            DownloadGamePassGameCache(gamePassGame);

            // Notify user that game has been added to the catalog
            if (notifyCatalogUpdates)
            {
                playniteApi.Notifications.Add(new NotificationMessage(
                    Guid.NewGuid().ToString(),
                    $"{gamePassGame.Name} has been added to the Game Pass catalog",
                    NotificationType.Info,
                    () => ProcessStarter.StartUrl($"msxbox://game/?productId={product.ProductId}")));
            }

            RestoreMediaPaths(gamePassGame);
        }

        private GamePassGame GetGamePassGameFromProduct(ProductType gameProductType, bool isPC, bool isConsole, bool isChildProduct, string parentProductId, CatalogProduct product, List<string> childSubproductsList)
        {
            var gamePassGame = new GamePassGame
            {
                BackgroundImage = $"{product.ProductId}_background.jpg",
                BackgroundImageUrl = string.Format("https:{0}", product.LocalizedProperties[0].Images.Where(x => x.ImagePurpose == ImagePurpose.SuperHeroArt)?.FirstOrDefault()?.Uri),
                Category = product.Properties.Category,
                Categories = product.Properties.Categories,
                CoverImage = $"{product.ProductId}_cover.jpg",
                CoverImageUrl = string.Format("https:{0}", product.LocalizedProperties[0].Images.Where(x => x.ImagePurpose == ImagePurpose.Poster)?.FirstOrDefault()?.Uri),
                CoverImageLowRes = $"{product.ProductId}_cover_low.jpg",
                Description = product.LocalizedProperties[0].ProductDescription,
                Name = NormalizeGameName(product.LocalizedProperties[0].ProductTitle),
                ProductId = product.ProductId,
                Publishers = CompaniesStringToList(product.LocalizedProperties[0].PublisherName),
                ReleaseDate = product.MarketProperties.FirstOrDefault().OriginalReleaseDate.UtcDateTime,
                ChildProducts = childSubproductsList,
                IsChildProduct = isChildProduct,
                ParentProductId = parentProductId,
                IsPC = isPC,
                IsConsole = isConsole
            };

            gamePassGame.GameId = product.Properties.PackageFamilyName;
            if (gamePassGame.GameId.IsNullOrEmpty())
            {
                gamePassGame.GameId = product.ProductId;
            }
            gamePassGame.ProductType = gameProductType;

            if (product.LocalizedProperties[0].DeveloperName.IsNullOrEmpty())
            {
                gamePassGame.Developers = gamePassGame.Publishers;
            }
            else
            {
                gamePassGame.Developers = CompaniesStringToList(product.LocalizedProperties[0].DeveloperName);
            }

            if (product.LocalizedProperties[0].Images.Any(x => x.ImagePurpose == ImagePurpose.BoxArt) == true)
            {
                gamePassGame.Icon = $"{product.ProductId}_icon.jpg";
                gamePassGame.IconUrl = string.Format("https:{0}", product.LocalizedProperties[0].Images.Where(x => x.ImagePurpose == ImagePurpose.BoxArt)?.FirstOrDefault()?.Uri);
            }
            else if (product.LocalizedProperties[0].Images.Any(x => x.ImagePurpose == ImagePurpose.Logo) == true)
            {
                gamePassGame.Icon = $"{product.ProductId}_icon.jpg";
                gamePassGame.IconUrl = string.Format("https:{0}", product.LocalizedProperties[0].Images.Where(x => x.ImagePurpose == ImagePurpose.Logo)?.FirstOrDefault()?.Uri);
            }

            return gamePassGame;
        }

        public List<GamePassGame> GetGamePassGamesList(GlobalProgressActionArgs progressArgs = null)
        {
            // Try to create cache directory in case it doesn't exist
            Directory.CreateDirectory(imageCachePath);

            if (FileSystem.FileExists(gameDataCachePath))
            {
                gamePassGamesList = JsonConvert.DeserializeObject<List<GamePassGame>>(File.ReadAllText(gameDataCachePath));
                foreach (var game in gamePassGamesList)
                {
                    game.TempIsFound = false;
                    game.HideConsolePlatform = !showConsoleGamesInBrowser;
                }
            }

            if (progressArgs != null) progressArgs.Text = "Downloading Game Pass PC catalog...";
            var gamePassCatalogDownload = GetGamepassCatalog(gamepassCatalogApiUrl);
            if (progressArgs?.CancelToken.IsCancellationRequested == true) return SaveCacheAndReturn();
            if (gamePassCatalogDownload != null)
            {
                ProcessGamePassCatalog(gamePassCatalogDownload, ProductType.Game, true, false, progressArgs);
            }

            if (progressArgs != null) progressArgs.Text = "Downloading EA Play PC catalog...";
            var gamePassEaCatalogDownload = GetGamepassCatalog(gamepassEaCatalogApiUrl);
            if (progressArgs?.CancelToken.IsCancellationRequested == true) return SaveCacheAndReturn();
            if (gamePassEaCatalogDownload != null)
            {
                ProcessGamePassCatalog(gamePassEaCatalogDownload, ProductType.EaGame, true, false, progressArgs);
            }

            if (enableConsoleCatalog)
            {
                if (progressArgs != null) progressArgs.Text = "Downloading Game Pass Console catalog...";
                var gamePassConsoleCatalogDownload = GetGamepassCatalog(gamepassConsoleCatalogApiUrl);
                if (progressArgs?.CancelToken.IsCancellationRequested == true) return SaveCacheAndReturn();
                if (gamePassConsoleCatalogDownload != null)
                {
                    ProcessGamePassCatalog(gamePassConsoleCatalogDownload, ProductType.Game, false, true, progressArgs);
                }

                if (progressArgs != null) progressArgs.Text = "Downloading EA Play Console catalog...";
                var gamePassEaConsoleCatalogDownload = GetGamepassCatalog(gamepassEaConsoleCatalogApiUrl);
                if (progressArgs?.CancelToken.IsCancellationRequested == true) return SaveCacheAndReturn();
                if (gamePassEaConsoleCatalogDownload != null)
                {
                    ProcessGamePassCatalog(gamePassEaConsoleCatalogDownload, ProductType.EaGame, false, true, progressArgs);
                }
            }

            CleanupRemovedGames();
            
            gamePassGamesList = gamePassGamesList
                .GroupBy(g => g.ProductId)
                .Select(g => g.OrderBy(x => x.ProductType == ProductType.Game ? 0 : 1).First())
                .GroupBy(g => g.Name.ToLower().Trim())
                .Select(group =>
                {
                    var mergedGame = group.OrderBy(x => x.IsChildProduct ? 1 : 0) // Prefer standalone over bundle child
                                          .ThenBy(x => x.IsPC ? 0 : 1)           // Prefer PC art over Console
                                          .ThenBy(x => x.ProductType == ProductType.Game ? 0 : 1)
                                          .First();
                    mergedGame.IsPC = group.Any(x => x.IsPC);
                    mergedGame.IsConsole = group.Any(x => x.IsConsole);
                    return mergedGame;
                })
                .ToList();

            return SaveCacheAndReturn();
        }

        private List<GamePassGame> SaveCacheAndReturn()
        {
            File.WriteAllText(gameDataCachePath, JsonConvert.SerializeObject(gamePassGamesList));
            return SetGamePassListFullPaths(gamePassGamesList);
        }

        private void ProcessGamePassCatalog (List<GamePassCatalogProduct> gamePassCatalog, ProductType gameProductType, bool isPC, bool isConsole, GlobalProgressActionArgs progressArgs = null)
        {
            if (progressArgs != null) progressArgs.Text = "Processing game updates and metadata...";
            var idsForDataRequest = new List<string>();

            foreach (var catalogItem in gamePassCatalog)
            {
                var games = gamePassGamesList.Where(x => x.ProductId == catalogItem.Id || (x.IsChildProduct && x.ParentProductId == catalogItem.Id)).ToList();
                foreach (var game in games)
                {
                    game.TempIsFound = true;
                    if (isPC) game.IsPC = true;
                    if (isConsole) game.IsConsole = true;
                }
            }

            foreach (GamePassCatalogProduct gamePassProduct in gamePassCatalog)
            {
                if (!gamePassGamesList.Any(x => x.ProductId.Equals(gamePassProduct.Id) || (x.IsChildProduct && x.ParentProductId == gamePassProduct.Id)))
                {
                    idsForDataRequest.Add(gamePassProduct.Id);
                }
            }

            if (!idsForDataRequest.Any())
            {
                return;
            }

            var bigIdsParam = string.Join(",", idsForDataRequest);
            var catalogDataApiUrl = string.Format(catalogDataApiBaseUrl, bigIdsParam, countryCode, languageCode);
            try
            {
                var downloadResult = HttpRequestFactory.GetHttpRequest().WithUrl(catalogDataApiUrl).DownloadString();
                if (progressArgs?.CancelToken.IsCancellationRequested == true) return;
                
                if (downloadResult.IsSuccess)
                {
                    AddGamesFromCatalogData(JsonConvert.DeserializeObject<CatalogData>(downloadResult.Content), true, gameProductType, isPC, isConsole, false, string.Empty, progressArgs);
                }
                else
                {
                    logger.Info($"Request {catalogDataApiUrl} not completed");
                }
            }
            catch (Exception e)
            {
                logger.Error(e, $"Error in ApiRequest {catalogDataApiUrl}");
            }
        }



        private void CleanupRemovedGames()
        {
            var gamesToRemove = gamePassGamesList.Where(g => !g.TempIsFound).ToList();
            foreach (var game in gamesToRemove)
            {
                var gameFilesPaths = new List<string>()
                {
                    Path.Combine(imageCachePath, game.BackgroundImage),
                    Path.Combine(imageCachePath, game.CoverImage),
                    Path.Combine(imageCachePath, game.CoverImageLowRes)
                };

                if (game.Icon != null)
                {
                    gameFilesPaths.AddMissing(Path.Combine(imageCachePath, game.Icon));
                }

                foreach (string filePath in gameFilesPaths)
                {
                    FileSystem.DeleteFileSafe(filePath);
                }

                var gameRemoved = false;
                if (removeExpiredGames)
                {
                    gameRemoved = xboxLibraryHelper.RemoveGamePassGame(game);
                    if (gameRemoved)
                    {
                        playniteApi.Notifications.Add(new NotificationMessage(
                        Guid.NewGuid().ToString(),
                            $"{game.Name} has been removed from the Game Pass catalog and Playnite library",
                            NotificationType.Info,
                            () => ProcessStarter.StartUrl($"msxbox://game/?productId={game.ProductId}")));
                    }
                }

                if (addExpiredTagToGames)
                {
                    xboxLibraryHelper.AddExpiredTag(game);
                }

                if (notifyCatalogUpdates && !gameRemoved)
                {
                    playniteApi.Notifications.Add(new NotificationMessage(
                    Guid.NewGuid().ToString(),
                        $"{game.Name} has been removed from the Game Pass catalog",
                        NotificationType.Info,
                        () => ProcessStarter.StartUrl($"msxbox://game/?productId={game.ProductId}")));
                }

                gamePassGamesList.Remove(game);
            }
        }

        public void DownloadGamePassGameCache(GamePassGame game)
        {
            game.CoverImageLowRes = Path.Combine(imageCachePath, game.CoverImageLowRes);
            if (!FileSystem.FileExists(game.CoverImageLowRes))
            {
                HttpRequestFactory.GetHttpFileRequest()
                    .WithUrl(string.Format("{0}?mode=scale&q=90&h=300&w=200", game.CoverImageUrl))
                    .WithDownloadTo(game.CoverImageLowRes)
                    .DownloadFile();
            }

            game.CoverImage = Path.Combine(imageCachePath, game.CoverImage);
            if (!FileSystem.FileExists(game.CoverImage))
            {
                HttpRequestFactory.GetHttpFileRequest()
                    .WithUrl(string.Format("{0}?mode=scale&q=90&h=900&w=600", game.CoverImageUrl))
                    .WithDownloadTo(game.CoverImage)
                    .DownloadFile();
            }

            if (game.Icon != null)
            {
                game.Icon = Path.Combine(imageCachePath, game.Icon);
                if (!FileSystem.FileExists(game.Icon))
                {
                    HttpRequestFactory.GetHttpFileRequest()
                        .WithUrl(string.Format("{0}?mode=scale&q=90&h=128&w=128", game.IconUrl))
                        .WithDownloadTo(game.Icon)
                        .DownloadFile();
                }
            }

            if (!string.IsNullOrEmpty(game.BackgroundImageUrl))
            {
                game.BackgroundImage = Path.Combine(imageCachePath, string.Format("{0}_background.jpg", game.ProductId));
                if (!FileSystem.FileExists(game.BackgroundImage))
                {
                    HttpRequestFactory.GetHttpFileRequest()
                        .WithUrl($"{game.BackgroundImageUrl}?mode=scale&q=90&h=1080&w=1920")
                        .WithDownloadTo(game.BackgroundImage)
                        .DownloadFile();
                }
            }
        }

        public GamePassGame RestoreMediaPaths(GamePassGame game)
        {
            game.CoverImageLowRes = Path.GetFileName(game.CoverImageLowRes);
            game.CoverImage = Path.GetFileName(game.CoverImage);
            if (game.Icon != null)
            {
                game.Icon = Path.GetFileName(game.Icon);
            }
            if (!string.IsNullOrEmpty(game.BackgroundImage))
            {
                game.BackgroundImage = Path.GetFileName(game.BackgroundImage);
            }

            return game;
        }

        public List<GamePassGame> SetGamePassListFullPaths(List<GamePassGame> cacheGamesList)
        {
            foreach (GamePassGame game in cacheGamesList.ToList())
            {
                game.CoverImageLowRes = Path.Combine(imageCachePath, Path.GetFileName(game.CoverImageLowRes));
                game.CoverImage = Path.Combine(imageCachePath, Path.GetFileName(game.CoverImage));
                if (game.Icon != null)
                {
                    game.Icon = Path.Combine(imageCachePath, Path.GetFileName(game.Icon));
                }
                if (!string.IsNullOrEmpty(game.BackgroundImage))
                {
                    game.BackgroundImage = Path.Combine(imageCachePath, Path.GetFileName(game.BackgroundImage));
                }
            }

            cacheGamesList.Sort((x, y) => x.Name.CompareTo(y.Name));
            return cacheGamesList;
        }
    }
}
